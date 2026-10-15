using CoGaze.Core;

namespace CoGaze.Worker.Cue
{
    /// <summary>
    /// Each visualisation's renderer lives in its own additive scene, so conditions differ only by
    /// which scene is loaded and each renderer exists once. The scene is derived from the condition;
    /// conditions.json names no scenes.
    /// </summary>
    public static class CueScenes
    {
        public static string For(Visualization v) => "Cue_" + v;

        /// <summary>The scene the current condition needs, or null when it shows no cue (no condition, or no gaze sent).</summary>
        public static string Wanted(IConditionSource condition) =>
            condition.HasCondition && condition.Current.SendsGaze ? For(condition.Current.Visualization) : null;
    }
}
