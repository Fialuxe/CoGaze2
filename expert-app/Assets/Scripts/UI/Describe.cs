using CoGaze.Core;

namespace CoGaze.Expert.UI
{
    /// <summary>Plain-language descriptions of conditions, so the screen says what the worker will see.</summary>
    static class Describe
    {
        public static string Kind(TrialKind k) =>
            k == TrialKind.Tutorial ? "Tutorial" : k == TrialKind.Practice ? "Practice" : "Main";

        public static string Vis(Visualization v) =>
            v == Visualization.Ray ? "Ray" : v == Visualization.Circle ? "Circle" : "Frustum";

        public static string Sigma(SigmaSource s) =>
            s == SigmaSource.Fixed ? "fixed" : s == SigmaSource.Calib ? "calibration" : "live";

        /// <summary>"beam gaze · Circle · size follows uncertainty (calibration)" / "no gaze shown".</summary>
        public static string Condition(in Condition c)
        {
            if (!c.SendsGaze) return "no gaze shown to the worker";
            string size = c.ShowUncertainty ? $"size follows uncertainty ({Sigma(c.SigmaSource)})" : "fixed size";
            return $"{c.DisplaySource} gaze · {Vis(c.Visualization)} · {size}";
        }
    }
}
