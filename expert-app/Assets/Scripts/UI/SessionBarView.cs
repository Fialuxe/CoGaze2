using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>Top bar: who, which condition, which trial, what state, how long.</summary>
    sealed class SessionBarView
    {
        readonly ISessionStatus _session;
        readonly IConditionSource _conditions;
        readonly IClock _clock;
        readonly Label _participant, _condition, _trial, _state, _timer;

        public VisualElement Root { get; }

        public SessionBarView(ISessionStatus session, IConditionSource conditions, IClock clock, bool rehearsal)
        {
            _session = session;
            _conditions = conditions;
            _clock = clock;

            Root = Theme.Box("cg-topbar");
            Root.Add(Theme.Label("CoGaze", "cg-wordmark"));
            Root.Add(Theme.Label("Expert", "cg-tag"));
            if (rehearsal) Root.Add(Theme.Label("REHEARSAL", "cg-tag", "cg-tag--rehearsal"));

            Root.Add(Chip("Participant", out _participant));
            Root.Add(Chip("Condition", out _condition));
            Root.Add(Chip("Trial", out _trial));
            Root.Add(Theme.Box("cg-grow"));
            _state = Theme.Label("", "cg-pill");
            Root.Add(_state);
            _timer = Theme.Label("00:00", "cg-timer");
            Root.Add(_timer);
        }

        static VisualElement Chip(string key, out Label value)
        {
            var chip = Theme.Box("cg-chip");
            chip.Add(Theme.Label(key, "cg-chip__key"));
            value = Theme.Label("—", "cg-chip__value");
            chip.Add(value);
            return chip;
        }

        public void Refresh()
        {
            _participant.text = _session.ParticipantId;
            if (_conditions.HasCondition)
            {
                var c = _conditions.Current;
                string sigma = c.ShowUncertainty ? " · σ " + SigmaName(c.SigmaSource) : "";
                _condition.text = $"{c.ConditionId} · {c.DisplaySource} · {c.Visualization.ToString().ToLowerInvariant()}{sigma}";
            }
            else
            {
                _condition.text = _session.AwaitingWorker ? "sending…" : "—";
            }
            _trial.text = _session.TrialIndex < 0
                ? $"0 / {_session.TrialCount}"
                : $"{_session.TrialIndex + 1} / {_session.TrialCount} · {_session.CurrentKind.ToString().ToLowerInvariant()}";

            _state.ClearClassList();
            _state.AddToClassList("cg-pill");
            if (_session.AwaitingWorker) Pill("WAITING FOR WORKER", "cg-pill--waiting");
            else switch (_session.State)
            {
                case TrialState.Idle: Pill("IDLE", null); break;
                case TrialState.Ready: Pill("READY", "cg-pill--ready"); break;
                case TrialState.InTrial: Pill("IN TRIAL", "cg-pill--intrial"); break;
                case TrialState.InterTrial: Pill("BETWEEN TRIALS", null); break;
                case TrialState.Finished: Pill("FINISHED", "cg-pill--finished"); break;
            }

            long s = (_clock.NowNs - _session.StateSinceNs) / 1_000_000_000L;
            _timer.text = $"{s / 60:00}:{s % 60:00}";
        }

        void Pill(string text, string cls)
        {
            _state.text = text;
            if (cls != null) _state.AddToClassList(cls);
        }

        static string SigmaName(SigmaSource s) =>
            s == SigmaSource.Fixed ? "fixed" : s == SigmaSource.Calib ? "calib" : "calib_live";
    }
}
