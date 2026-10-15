namespace CoGaze.Core
{
    /// <summary>The gaze message sent to the worker (SRS 3.3). Unreliable delivery.</summary>
    public readonly struct GazeCue
    {
        public readonly uint Seq;
        public readonly string Source;
        public readonly bool Valid;
        public readonly GazeMode Mode;
        public readonly float U;
        public readonly float V;
        public readonly long VideoFrameId;
        public readonly float SigmaDeg;
        public readonly long TExpNs;

        public GazeCue(uint seq, string source, bool valid, GazeMode mode, float u, float v,
                       long videoFrameId, float sigmaDeg, long tExpNs)
        {
            Seq = seq;
            Source = source;
            Valid = valid;
            Mode = mode;
            U = u;
            V = v;
            VideoFrameId = videoFrameId;
            SigmaDeg = sigmaDeg;
            TExpNs = tExpNs;
        }
    }
}
