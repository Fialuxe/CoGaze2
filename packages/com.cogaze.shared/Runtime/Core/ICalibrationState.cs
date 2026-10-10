namespace CoGaze.Core
{
    /// <summary>Calibration outcome, read by the sigma policies (EXP-F-046, EXP-F-048).</summary>
    public interface ICalibrationState
    {
        bool HasResult { get; }
        float MeanValidationErrorDeg { get; }
    }
}
