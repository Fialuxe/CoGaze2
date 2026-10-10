using System;
using CoGaze.Core;

namespace CoGaze.Worker.Session
{
    /// <summary>
    /// The worker's side of the control channel (#26): applies the expert's Condition and echoes its
    /// id (the expert does not start a trial until the ids agree), applies Trial messages, answers
    /// ClockPing with this device's clock (COM-F-003, WRK-F-036), and hands the Session folder name
    /// on. Everything is logged to the worker's events.csv. The worker never decides any of these.
    /// </summary>
    public sealed class ControlResponder
    {
        readonly IMessageChannel _channel;
        readonly IClock _clock;
        readonly ILogSink _log;
        readonly Action<Condition> _onCondition;
        readonly Action<TrialMessage> _onTrial;
        readonly Action<string> _onSession;

        public ControlResponder(IMessageChannel channel, IClock clock, ILogSink log, Action<Condition> onCondition,
                                Action<TrialMessage> onTrial, Action<string> onSession)
        {
            _channel = channel;
            _clock = clock;
            _log = log;
            _onCondition = onCondition;
            _onTrial = onTrial;
            _onSession = onSession;
        }

        /// <summary>Handles everything that arrived. Receive time is taken as messages are drained, once per frame.</summary>
        public void Drain()
        {
            while (_channel.TryReceive(out var m)) Handle(m, _clock.NowNs);
        }

        void Handle(in ControlMessage m, long now)
        {
            switch (m.Kind)
            {
                case MessageKind.Session:
                    _onSession(m.Text);
                    _log.Event(now, EventKinds.SessionStart, m.Text);
                    break;
                case MessageKind.Condition:
                    _onCondition(m.Condition);
                    _channel.Send(ControlMessage.Ack(m.Condition.ConditionId));
                    _log.Event(now, EventKinds.ConditionAck, m.Condition.ConditionId);
                    break;
                case MessageKind.Trial:
                    _onTrial(m.Trial);
                    _log.Event(now, KindOf(m.Trial.Phase), $"trial={m.Trial.TrialIndex} condition={m.Trial.ConditionId} kind={m.Trial.Kind}");
                    break;
                case MessageKind.ClockPing when !m.Ping.IsPong:
                    _channel.Send(ControlMessage.Of(new ClockPing(m.Ping.Seq, m.Ping.TPingNs, now, Math.Max(now + 1, _clock.NowNs))));
                    break;
            }
        }

        static string KindOf(TrialPhase phase)
        {
            switch (phase)
            {
                case TrialPhase.Start: return EventKinds.TrialStart;
                case TrialPhase.End: return EventKinds.TrialEnd;
                default: return EventKinds.TrialAbort;
            }
        }
    }
}
