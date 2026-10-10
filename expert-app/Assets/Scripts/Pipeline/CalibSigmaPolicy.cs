using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>
    /// sigma_source = calib: the mean validation error from the last calibration. Before any
    /// calibration, the configured default (and the checklist should show calibration missing).
    /// </summary>
    public sealed class CalibSigmaPolicy : ISigmaPolicy
    {
        readonly ICalibrationState _calibration;
        readonly float _defaultDeg;

        public CalibSigmaPolicy(ICalibrationState calibration, float defaultDeg)
        {
            _calibration = calibration;
            _defaultDeg = defaultDeg;
        }

        public float SigmaDeg(in GazeSample sample, in GazeRay ray) =>
            _calibration.HasResult ? _calibration.MeanValidationErrorDeg : _defaultDeg;
    }
}
