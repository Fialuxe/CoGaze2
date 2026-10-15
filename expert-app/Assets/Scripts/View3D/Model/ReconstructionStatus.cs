namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// The receiver's counters, written on the main thread as updates are applied and read by the
    /// checklist row and the stage label. A state holder, not a value in flight.
    /// </summary>
    public sealed class ReconstructionStatus
    {
        public ReconstructionSource Source { get; }
        public ReconstructionMode Mode => Source.Mode;
        public LinkState Link { get; set; } = LinkState.Idle;
        /// <summary>Why <see cref="LinkState.Failed"/>, in plain words.</summary>
        public string Problem { get; private set; } = "";
        /// <summary>Chunks currently shown.</summary>
        public int Chunks { get; private set; }
        /// <summary>Updates applied since start.</summary>
        public int Updates { get; private set; }
        /// <summary>Replay only: how many files there are.</summary>
        public int UpdatesTotal { get; set; }
        /// <summary>IClock time of the last applied update; -1 before the first.</summary>
        public long LastUpdateNs { get; private set; } = -1;
        /// <summary>Chunks whose payload did not decode or whose update did not parse.</summary>
        public int DecodeFailures { get; set; }

        public ReconstructionStatus(ReconstructionSource source) => Source = source;

        public void Applied(int chunks, long nowNs)
        {
            Chunks = chunks;
            Updates++;
            LastUpdateNs = nowNs;
        }

        /// <summary>Chunk count changed without an update (an epoch reset clears everything).</summary>
        public void SetChunks(int chunks) => Chunks = chunks;

        public void Fail(string problem)
        {
            Link = LinkState.Failed;
            Problem = problem ?? "";
        }
    }
}
