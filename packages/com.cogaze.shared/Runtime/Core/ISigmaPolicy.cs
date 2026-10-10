namespace CoGaze.Core
{
    /// <summary>How sigma is decided: fixed / calib / calib_live (EXP-F-046).</summary>
    public interface ISigmaPolicy
    {
        float SigmaDeg(in GazeSample sample, in GazeRay ray);
    }
}
