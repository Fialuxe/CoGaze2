using System;
using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Expert.Experiment
{
    /// <summary>
    /// The only writer of the current condition. States: Idle → Ready → InTrial → InterTrial → Ready …
    /// → Finished. The condition changes only when leaving Idle/InterTrial (EXP-F-034), and a trial
    /// cannot start until the worker has echoed the same condition_id — so the two sides never run a
    /// trial under different conditions.
    /// </summary>
    public sealed class TrialRunner : IConditionSource, ISessionStatus, IExperimentControl
    {
        public const string WorkerConditionKey = "worker_condition";

        readonly IReadOnlyList<PlanEntry> _plan;
        readonly IReadOnlyDictionary<string, Condition> _conditions;
        readonly IMessageChannel _channel;
        readonly ILogSink _log;
        readonly IClock _clock;
        readonly IHealthBoard _health;
        readonly string _sessionDir;

        Condition _current;
        bool _hasCurrent;
        string _pendingId;
        int _nextIndex;
        int _completedMain;
        bool _flagged;

        public TrialRunner(string participantId, IReadOnlyList<PlanEntry> plan, IReadOnlyDictionary<string, Condition> conditions,
                           IMessageChannel channel, ILogSink log, IClock clock, IHealthBoard health, string sessionDir,
                           int startIndex = 0)
        {
            ParticipantId = participantId;
            _plan = plan;
            _conditions = conditions;
            _channel = channel;
            _log = log;
            _clock = clock;
            _health = health;
            _sessionDir = sessionDir;
            _nextIndex = startIndex;
            TrialIndex = -1;
            StateSinceNs = clock.NowNs;
            _health.Set(new HealthItem(WorkerConditionKey, "Worker condition", HealthLevel.Ok, "—"));
        }

        /// <summary>Raised after a trial ends or aborts; the log sink flushes here.</summary>
        public event Action TrialClosed;

        // ---- IConditionSource ----
        public bool HasCondition => _hasCurrent;
        public Condition Current => _current;
        public bool InTrial => State == TrialState.InTrial;

        // ---- ISessionStatus ----
        public string ParticipantId { get; }
        public TrialState State { get; private set; } = TrialState.Idle;
        public bool AwaitingWorker => _pendingId != null;
        public int TrialIndex { get; private set; }
        public int TrialCount => _plan.Count;
        public TrialKind CurrentKind => TrialIndex >= 0 ? _plan[TrialIndex].Kind : TrialKind.Main;
        public long StateSinceNs { get; private set; }
        public bool CanStartTrial => State == TrialState.Ready && !AwaitingWorker && _health.AllOk;

        public IReadOnlyList<PlanItem> Plan
        {
            get
            {
                var items = new List<PlanItem>(_plan.Count);
                for (int i = 0; i < _plan.Count; i++)
                {
                    bool current = i == TrialIndex && (State == TrialState.Ready || State == TrialState.InTrial);
                    var state = current ? PlanItemState.Current : i < _nextIndex ? PlanItemState.Done : PlanItemState.Upcoming;
                    items.Add(new PlanItem(i, _plan[i].Kind, _conditions[_plan[i].ConditionId], state));
                }
                return items;
            }
        }

        public string NextConditionId => _nextIndex < _plan.Count ? _plan[_nextIndex].ConditionId : null;

        // ---- IExperimentControl ----
        public void PrepareNext()
        {
            if (State != TrialState.Idle && State != TrialState.InterTrial && !(State == TrialState.Ready && AwaitingWorker))
                return;   // never change the condition mid-trial or once agreed
            if (_nextIndex >= _plan.Count)
            {
                Enter(TrialState.Finished);
                _log.Event(_clock.NowNs, EventKinds.SessionEnd, $"completed_main={_completedMain}");
                return;
            }
            var entry = _plan[_nextIndex];
            _pendingId = entry.ConditionId;
            _hasCurrent = false;   // nothing reaches the worker until it confirms the new condition
            _channel.Send(ControlMessage.Of(_conditions[entry.ConditionId]));
            _log.Event(_clock.NowNs, EventKinds.ConditionSent, $"{entry.ConditionId} index={_nextIndex}");
            TrialIndex = _nextIndex;
            Enter(TrialState.Ready);
        }

        public void StartTrial()
        {
            if (!CanStartTrial) return;
            _flagged = false;
            SendTrial(TrialPhase.Start);
            _log.Event(_clock.NowNs, EventKinds.TrialStart, TrialDetail());
            Enter(TrialState.InTrial);
        }

        public void EndTrial()
        {
            if (State != TrialState.InTrial) return;
            SendTrial(TrialPhase.End);
            _log.Event(_clock.NowNs, EventKinds.TrialEnd, TrialDetail() + (_flagged ? " flagged=1" : ""));
            if (_plan[TrialIndex].Kind == TrialKind.Main) _completedMain++;
            _nextIndex = TrialIndex + 1;
            if (_sessionDir != null) ProgressStore.Save(_sessionDir, ParticipantId, _nextIndex, _completedMain);
            Enter(TrialState.InterTrial);
            TrialClosed?.Invoke();
        }

        public void AbortTrial()
        {
            if (State != TrialState.InTrial) return;
            SendTrial(TrialPhase.Abort);
            _log.Event(_clock.NowNs, EventKinds.TrialAbort, TrialDetail());
            _nextIndex = TrialIndex;   // run the same entry again (EXP-F-058)
            Enter(TrialState.InterTrial);
            TrialClosed?.Invoke();
        }

        /// <summary>Feed every message from the worker channel.</summary>
        public void Handle(in ControlMessage m)
        {
            if (m.Kind != MessageKind.ConditionAck || _pendingId == null) return;
            if (m.Text == _pendingId)
            {
                _current = _conditions[_pendingId];
                _hasCurrent = true;
                _pendingId = null;
                _log.Event(_clock.NowNs, EventKinds.ConditionAck, m.Text);
                _health.Set(new HealthItem(WorkerConditionKey, "Worker condition", HealthLevel.Ok, m.Text));
            }
            else
            {
                _log.Event(_clock.NowNs, EventKinds.ConditionMismatch, $"sent={_pendingId} echoed={m.Text}");
                _health.Set(new HealthItem(WorkerConditionKey, "Worker condition", HealthLevel.Error, m.Text,
                    $"Worker applied {m.Text}, expected {_pendingId}.",
                    "Press N to resend the condition; restart the worker app if it repeats."));
            }
        }

        /// <summary>Call every frame: marks a trial during which any checklist row went red.</summary>
        public void Tick()
        {
            if (State == TrialState.InTrial && !_flagged && !_health.AllOk)
            {
                _flagged = true;
                _log.Event(_clock.NowNs, EventKinds.TrialFlagged, TrialDetail());
            }
        }

        void SendTrial(TrialPhase phase)
        {
            var e = _plan[TrialIndex];
            _channel.Send(ControlMessage.Of(new TrialMessage(TrialIndex, phase, e.Kind, e.ConditionId, _current.Task, "")));
        }

        string TrialDetail()
        {
            var e = _plan[TrialIndex];
            return $"index={TrialIndex} condition={e.ConditionId} kind={e.Kind.ToString().ToLowerInvariant()}";
        }

        void Enter(TrialState s)
        {
            State = s;
            StateSinceNs = _clock.NowNs;
        }
    }
}
