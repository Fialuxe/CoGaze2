using System.Numerics;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>
    /// One /cogaze/gaze message: the webcam estimator's output in OpenCV camera coordinates (mm,
    /// COM-F-004). It becomes a screen point only through the calibration mapping (EXP-F-020, #32).
    /// </summary>
    public readonly struct EstimatorSample
    {
        public readonly long TCaptureNs;
        public readonly long TSentNs;
        public readonly long TReceiveNs;
        public readonly long FrameIdx;
        public readonly string Source;
        public readonly bool Valid;
        public readonly string Mode;
        public readonly Vector3 Gaze;
        public readonly Vector3 OriginMm;
        public readonly Vector3 HeadRotDeg;
        public readonly Vector3 HeadPosMm;
        public readonly float SigmaDeg;

        EstimatorSample(long tCaptureNs, long tSentNs, long tReceiveNs, long frameIdx, string source, bool valid, string mode,
                        Vector3 gaze, Vector3 originMm, Vector3 headRotDeg, Vector3 headPosMm, float sigmaDeg)
        {
            TCaptureNs = tCaptureNs;
            TSentNs = tSentNs;
            TReceiveNs = tReceiveNs;
            FrameIdx = frameIdx;
            Source = source;
            Valid = valid;
            Mode = mode;
            Gaze = gaze;
            OriginMm = originMm;
            HeadRotDeg = headRotDeg;
            HeadPosMm = headPosMm;
            SigmaDeg = sigmaDeg;
        }

        internal static EstimatorSample From(OscMessage m, long tReceiveNs) => new EstimatorSample(
            m.Long(0), m.Long(1), tReceiveNs, m.Long(2), m.Str(3), m.Int(4) != 0, m.Str(5),
            new Vector3(m.Float(6), m.Float(7), m.Float(8)),
            new Vector3(m.Float(9), m.Float(10), m.Float(11)),
            new Vector3(m.Float(12), m.Float(13), m.Float(14)),
            new Vector3(m.Float(15), m.Float(16), m.Float(17)),
            m.Float(18));
    }
}
