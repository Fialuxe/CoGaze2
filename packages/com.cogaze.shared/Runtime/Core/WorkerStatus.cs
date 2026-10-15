namespace CoGaze.Core
{
    /// <summary>Sent by the worker at 1 Hz (WRK-F-037).</summary>
    public readonly struct WorkerStatus
    {
        public readonly float FrameRateHz;
        public readonly AlignmentState Alignment;
        public readonly bool Recording;
        public readonly long TWorkerNs;

        public WorkerStatus(float frameRateHz, AlignmentState alignment, bool recording, long tWorkerNs)
        {
            FrameRateHz = frameRateHz;
            Alignment = alignment;
            Recording = recording;
            TWorkerNs = tWorkerNs;
        }
    }
}
