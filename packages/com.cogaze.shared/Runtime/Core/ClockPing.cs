namespace CoGaze.Core
{
    /// <summary>One clock round trip (COM-F-003). The pinger fills Seq and TPingNs; the responder adds its two times.</summary>
    public readonly struct ClockPing
    {
        public readonly uint Seq;
        /// <summary>Pinger clock, when the ping left.</summary>
        public readonly long TPingNs;
        /// <summary>Responder clock, when the ping arrived. 0 on the outgoing ping.</summary>
        public readonly long TReceiveNs;
        /// <summary>Responder clock, when the pong left. 0 on the outgoing ping.</summary>
        public readonly long TReplyNs;

        public ClockPing(uint seq, long tPingNs, long tReceiveNs = 0, long tReplyNs = 0)
        {
            Seq = seq;
            TPingNs = tPingNs;
            TReceiveNs = tReceiveNs;
            TReplyNs = tReplyNs;
        }

        public bool IsPong => TReplyNs != 0;
    }
}
