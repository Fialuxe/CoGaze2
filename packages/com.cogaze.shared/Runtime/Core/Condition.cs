namespace CoGaze.Core
{
    /// <summary>One experimental condition (EXP-F-055). Validated on load, immutable afterwards.</summary>
    public readonly struct Condition
    {
        /// <summary>The display source meaning "send no gaze at all" (EXP-F-031).</summary>
        public const string NoSource = "none";

        public readonly string ConditionId;
        public readonly string DisplaySource;
        public readonly Visualization Visualization;
        public readonly bool ShowUncertainty;
        public readonly SigmaSource SigmaSource;
        /// <summary>NaN unless SigmaSource is Fixed.</summary>
        public readonly float FixedSigmaDeg;
        /// <summary>Name of a filter profile in app.json.</summary>
        public readonly string Filter;
        public readonly string Task;

        public Condition(string conditionId, string displaySource, Visualization visualization, bool showUncertainty,
                         SigmaSource sigmaSource, float fixedSigmaDeg, string filter, string task)
        {
            ConditionId = conditionId;
            DisplaySource = displaySource;
            Visualization = visualization;
            ShowUncertainty = showUncertainty;
            SigmaSource = sigmaSource;
            FixedSigmaDeg = fixedSigmaDeg;
            Filter = filter;
            Task = task;
        }

        public bool SendsGaze => DisplaySource != NoSource;
    }
}
