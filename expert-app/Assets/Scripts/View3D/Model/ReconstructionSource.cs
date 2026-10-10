namespace CoGaze.Expert.View3D.Model
{
    /// <summary>Where chunk updates come from (app.json "reconstruction"), as the bootstrap resolved it.</summary>
    public readonly struct ReconstructionSource
    {
        /// <summary>ws://…/expert of server/service.py; empty when replaying.</summary>
        public readonly string Url;
        /// <summary>Absolute directory of .cgzm files; empty when live.</summary>
        public readonly string StagingDir;
        public readonly float ReplayIntervalS;

        public ReconstructionSource(string url, string stagingDir, float replayIntervalS)
        {
            Url = url ?? "";
            StagingDir = stagingDir ?? "";
            ReplayIntervalS = replayIntervalS;
        }

        public ReconstructionMode Mode =>
            Url.Length > 0 ? ReconstructionMode.Live : StagingDir.Length > 0 ? ReconstructionMode.Replay : ReconstructionMode.NotConfigured;
    }
}
