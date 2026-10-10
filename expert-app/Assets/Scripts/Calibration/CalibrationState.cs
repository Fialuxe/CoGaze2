using CoGaze.Core;

namespace CoGaze.Expert.Calibration
{
    /// <summary>
    /// Holds the latest validation result for the sigma policies. The calibration procedure itself —
    /// points, head prompts, sample windows, ridge mapping, validation in degrees — is #32.
    /// </summary>
    public sealed class CalibrationState : ICalibrationState
    {
        public bool HasResult { get; private set; }
        public float MeanValidationErrorDeg { get; private set; }

        public void SetResult(float meanValidationErrorDeg)
        {
            MeanValidationErrorDeg = meanValidationErrorDeg;
            HasResult = true;
        }
    }
}
