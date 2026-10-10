namespace CoGaze.Expert.Pipeline
{
    /// <summary>One-Euro parameters (EXP-F-041). Disabled passes samples through unchanged.</summary>
    public readonly struct FilterSettings
    {
        public readonly bool Enabled;
        public readonly float MinCutoffHz;
        public readonly float Beta;
        public readonly float DCutoffHz;

        public FilterSettings(bool enabled, float minCutoffHz, float beta, float dCutoffHz)
        {
            Enabled = enabled;
            MinCutoffHz = minCutoffHz;
            Beta = beta;
            DCutoffHz = dCutoffHz;
        }

        public static FilterSettings PassThrough => new FilterSettings(false, 1, 0, 1);
    }
}
