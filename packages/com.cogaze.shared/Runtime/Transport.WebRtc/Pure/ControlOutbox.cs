using System.Collections.Generic;
using CoGaze.Core;
using CoGaze.Protocol;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Control messages sent while the link is down. Messages that change state on the other side
    /// (Session, Condition, ConditionAck, Trial, WorkerEvent) are held and delivered in order on
    /// reconnect, so "reliable" survives an outage; time-stamped ones (ClockPing, WorkerStatus) are
    /// dropped, because delivered late they would report a wrong time. Bounded: past the limit the
    /// oldest is dropped and counted.
    /// </summary>
    public sealed class ControlOutbox
    {
        readonly Queue<byte[]> _held = new Queue<byte[]>();
        readonly int _limit;

        public ControlOutbox(int limit = 64)
        {
            _limit = limit;
        }

        public int Count => _held.Count;
        public int Dropped { get; private set; }

        public static bool Holds(MessageKind kind) => kind != MessageKind.ClockPing && kind != MessageKind.WorkerStatus;

        /// <summary>Queues the message if it is worth delivering late; returns whether it was kept.</summary>
        public bool Hold(in ControlMessage message)
        {
            if (!Holds(message.Kind)) return false;
            _held.Enqueue(MessageCodec.Encode(message));
            while (_held.Count > _limit)
            {
                _held.Dequeue();
                Dropped++;
            }
            return true;
        }

        public bool TryTake(out byte[] bytes)
        {
            bytes = _held.Count > 0 ? _held.Dequeue() : null;
            return bytes != null;
        }
    }
}
