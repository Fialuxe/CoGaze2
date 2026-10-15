using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Clock
{
    /// <summary>
    /// Estimates remote-minus-local clock offset from ping/pong round trips, trusting only the one
    /// with the shortest round trip in a sliding window (COM-F-003): the shorter the trip, the less
    /// room for asymmetric delay.
    /// </summary>
    public sealed class ClockOffsetEstimator
    {
        public readonly struct RoundTrip
        {
            public readonly uint Seq;
            public readonly long RttNs;
            public readonly long OffsetNs;

            public RoundTrip(uint seq, long rttNs, long offsetNs)
            {
                Seq = seq;
                RttNs = rttNs;
                OffsetNs = offsetNs;
            }
        }

        readonly int _window;
        readonly Queue<RoundTrip> _trips = new Queue<RoundTrip>();

        public ClockOffsetEstimator(int window = 20)
        {
            _window = window;
        }

        public bool HasEstimate => _trips.Count > 0;

        /// <summary>Records a pong received at local time tLocalReceiveNs and returns that trip.</summary>
        public RoundTrip Add(in ClockPing pong, long tLocalReceiveNs)
        {
            long rtt = (tLocalReceiveNs - pong.TPingNs) - (pong.TReplyNs - pong.TReceiveNs);
            long offset = ((pong.TReceiveNs - pong.TPingNs) + (pong.TReplyNs - tLocalReceiveNs)) / 2;
            var trip = new RoundTrip(pong.Seq, rtt, offset);
            _trips.Enqueue(trip);
            while (_trips.Count > _window) _trips.Dequeue();
            return trip;
        }

        /// <summary>The window's minimum-RTT trip.</summary>
        public RoundTrip Best
        {
            get
            {
                RoundTrip best = default;
                bool first = true;
                foreach (var t in _trips)
                {
                    if (first || t.RttNs < best.RttNs) best = t;
                    first = false;
                }
                return best;
            }
        }
    }
}
