using System.Collections.Generic;
using CoGaze.Core;
using CoGaze.Protocol;

namespace CoGaze.Transport.Loopback
{
    /// <summary>
    /// Stands in for the worker during rehearsal: acknowledges conditions, reports status at 1 Hz and
    /// answers clock pings, all inside the expert process. Every message is pushed through
    /// MessageCodec, so rehearsal exercises the real wire format.
    /// </summary>
    public sealed class LoopbackTransport : ICueSender, IMessageChannel
    {
        readonly IClock _clock;
        readonly Queue<byte[]> _toExpert = new Queue<byte[]>();
        long _nextStatusNs;

        public LoopbackTransport(IClock clock)
        {
            _clock = clock;
        }

        /// <summary>Acknowledge conditions with a wrong id, to rehearse the mismatch path.</summary>
        public bool AckMismatch { get; set; }

        /// <summary>Reported in WorkerStatus.</summary>
        public AlignmentState Alignment { get; set; } = AlignmentState.Aligned;

        /// <summary>Simulated worker clock minus expert clock.</summary>
        public long WorkerClockOffsetNs { get; set; }

        public bool IsConnected { get; set; } = true;

        public int CuesReceived { get; private set; }
        public GazeCue LastCue { get; private set; }
        public List<ControlMessage> Received { get; } = new List<ControlMessage>();

        public void Send(in GazeCue cue)
        {
            if (!IsConnected) return;
            if (!MessageCodec.TryDecode(MessageCodec.Encode(cue), out GazeCue decoded)) return;
            CuesReceived++;
            LastCue = decoded;
        }

        public void Send(in ControlMessage message)
        {
            if (!IsConnected) return;
            if (!MessageCodec.TryDecode(MessageCodec.Encode(message), out ControlMessage m)) return;
            Received.Add(m);
            long workerNow = _clock.NowNs + WorkerClockOffsetNs;
            switch (m.Kind)
            {
                case MessageKind.Condition:
                    string id = AckMismatch ? m.Condition.ConditionId + "-mismatch" : m.Condition.ConditionId;
                    Reply(ControlMessage.Ack(id));
                    break;
                case MessageKind.ClockPing when !m.Ping.IsPong:
                    Reply(ControlMessage.Of(new ClockPing(m.Ping.Seq, m.Ping.TPingNs, workerNow, workerNow + 1)));
                    break;
            }
        }

        public bool TryReceive(out ControlMessage message)
        {
            long now = _clock.NowNs;
            if (IsConnected && now >= _nextStatusNs)
            {
                _nextStatusNs = now + 1_000_000_000L;
                Reply(ControlMessage.Of(new WorkerStatus(72f, Alignment, true, now + WorkerClockOffsetNs)));
            }
            message = default;
            return _toExpert.Count > 0 && MessageCodec.TryDecode(_toExpert.Dequeue(), out message);
        }

        void Reply(in ControlMessage m) => _toExpert.Enqueue(MessageCodec.Encode(m));
    }
}
