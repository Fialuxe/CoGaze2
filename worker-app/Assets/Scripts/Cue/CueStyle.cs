namespace CoGaze.Worker.Cue
{
    /// <summary>
    /// Sizes used when uncertainty is not shown, and the fade. The same in every condition
    /// (WRK-F-025); only the visualisation and the sigma-driven size change between conditions.
    /// </summary>
    public readonly struct CueStyle
    {
        public readonly float DefaultSigmaDeg;
        public readonly float FadeS;
        /// <summary>A cue older than this is treated as lost and fades.</summary>
        public readonly float StaleS;

        public CueStyle(float defaultSigmaDeg, float fadeS = 0.2f, float staleS = 0.5f)
        {
            DefaultSigmaDeg = defaultSigmaDeg;
            FadeS = fadeS;
            StaleS = staleS;
        }
    }
}
