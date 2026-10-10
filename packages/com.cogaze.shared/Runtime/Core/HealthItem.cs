namespace CoGaze.Core
{
    /// <summary>
    /// One checklist row. When not Ok, Problem says what happened and Action what the experimenter
    /// does next — one sentence each.
    /// </summary>
    public readonly struct HealthItem
    {
        public readonly string Key;
        public readonly string Label;
        public readonly HealthLevel Level;
        /// <summary>Short live value, e.g. "29.8 fps".</summary>
        public readonly string Value;
        public readonly string Problem;
        public readonly string Action;

        public HealthItem(string key, string label, HealthLevel level, string value = "", string problem = "", string action = "")
        {
            Key = key;
            Label = label;
            Level = level;
            Value = value ?? "";
            Problem = problem ?? "";
            Action = action ?? "";
        }
    }
}
