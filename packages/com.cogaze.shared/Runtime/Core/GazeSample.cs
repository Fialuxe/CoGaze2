namespace CoGaze.Core
{
    /// <summary>
    /// One gaze reading from one source, already in screen pixels of the expert monitor
    /// (top-left origin, +x right, +y down, physical px — COM-F-006).
    /// </summary>
    public readonly struct GazeSample
    {
        /// <summary>"beam", "tobii", "webcam:&lt;estimator&gt;", "replay" — matches Condition.DisplaySource.</summary>
        public readonly string Source;
        /// <summary>Time the source captured the sample, expert clock.</summary>
        public readonly long TCaptureNs;
        /// <summary>Time this process received it, expert clock.</summary>
        public readonly long TReceiveNs;
        public readonly bool Valid;
        public readonly float XPx;
        public readonly float YPx;
        public readonly GazeMode Mode;
        /// <summary>Uncertainty reported by the source itself, NaN when it reports none.</summary>
        public readonly float SourceSigmaDeg;
        /// <summary>Source-specific confidence 0..3 (Beam), -1 when the source has none.</summary>
        public readonly int Confidence;

        public GazeSample(string source, long tCaptureNs, long tReceiveNs, bool valid, float xPx, float yPx,
                          GazeMode mode = GazeMode.Eye, float sourceSigmaDeg = float.NaN, int confidence = -1)
        {
            Source = source;
            TCaptureNs = tCaptureNs;
            TReceiveNs = tReceiveNs;
            Valid = valid;
            XPx = xPx;
            YPx = yPx;
            Mode = mode;
            SourceSigmaDeg = sourceSigmaDeg;
            Confidence = confidence;
        }

        /// <summary>Same sample at a different screen position (the filtered copy).</summary>
        public GazeSample WithScreen(float xPx, float yPx) =>
            new GazeSample(Source, TCaptureNs, TReceiveNs, Valid, xPx, yPx, Mode, SourceSigmaDeg, Confidence);
    }
}
