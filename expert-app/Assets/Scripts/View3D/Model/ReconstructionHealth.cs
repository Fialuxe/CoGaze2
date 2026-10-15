using CoGaze.Core;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// What the reconstruction link looks like to the experimenter: one checklist row and the label
    /// on the 3D stage. Pure, so every state can be tested without Unity or a server.
    /// </summary>
    public static class ReconstructionHealth
    {
        public const string Key = "reconstruction";
        const string Label = "3D reconstruction";
        const long NsPerS = 1_000_000_000L;

        public static HealthItem Evaluate(ReconstructionStatus s, long nowNs)
        {
            switch (s.Mode)
            {
                case ReconstructionMode.NotConfigured:
                    // Only reachable with view "2d" (config refuses 3d without a source), so it must not block a trial.
                    return new HealthItem(Key, Label, HealthLevel.Ok, "not configured");
                case ReconstructionMode.Live when s.Link == LinkState.Failed:
                    return new HealthItem(Key, Label, HealthLevel.Error, "offline",
                        $"Cannot reach the reconstruction service at {s.Source.Url}: {s.Problem}.",
                        "Start server/service.py on the GPU PC and check reconstruction.url in app.json; it retries every few seconds.");
                case ReconstructionMode.Replay when s.Link == LinkState.Failed:
                    return new HealthItem(Key, Label, HealthLevel.Error, "no data",
                        $"Nothing to replay: {s.Problem}.",
                        "Run server/make_synthetic_chunks.py, or point reconstruction.staging_dir at a folder of .cgzm files.");
            }
            if (s.Link == LinkState.Idle || s.Link == LinkState.Connecting)
                return new HealthItem(Key, Label, HealthLevel.Unknown, s.Mode == ReconstructionMode.Live ? "connecting…" : "starting…");

            string value = Summary(s, nowNs);
            if (s.DecodeFailures > 0)
                return new HealthItem(Key, Label, HealthLevel.Warning, value,
                    $"{s.DecodeFailures} chunk(s) could not be decoded and are missing from the 3D view.",
                    "Check the server and this app use the same chunk format (server/chunks.py).");
            return new HealthItem(Key, Label, HealthLevel.Ok, value);
        }

        /// <summary>The label on the stage in 3D mode: what is being shown, so an empty view is never ambiguous.</summary>
        public static string StageLabel(ReconstructionStatus s, long nowNs)
        {
            const string head = "3D VIEW  ·  ";
            switch (s.Mode)
            {
                case ReconstructionMode.NotConfigured:
                    return head + "no reconstruction configured — set \"reconstruction\" in app.json";
                case ReconstructionMode.Live:
                    if (s.Link == LinkState.Failed)
                        return head + "OFFLINE — reconstruction service not reachable" + (s.Chunks > 0 ? $"  ·  showing last {s.Chunks} chunks" : "");
                    if (s.Link != LinkState.Connected) return head + "connecting to the reconstruction service…";
                    if (s.Updates == 0) return head + "LIVE  ·  waiting for data from the worker";
                    return head + $"LIVE  ·  {s.Chunks} chunks  ·  updated {Age(nowNs - s.LastUpdateNs)}";
                default:
                    if (s.Link == LinkState.Failed) return head + "no replay data — " + s.Problem;
                    if (s.Link == LinkState.ReplayDone) return head + $"REPLAY (finished)  ·  {s.Chunks} chunks";
                    if (s.Updates == 0) return head + "REPLAY  ·  loading…";
                    return head + $"REPLAY  ·  {s.Updates} / {s.UpdatesTotal} updates  ·  {s.Chunks} chunks";
            }
        }

        static string Summary(ReconstructionStatus s, long nowNs)
        {
            if (s.Mode == ReconstructionMode.Replay)
                return s.Link == LinkState.ReplayDone ? $"replay done · {s.Chunks} chunks" : $"replay · {s.Updates}/{s.UpdatesTotal} · {s.Chunks} chunks";
            return s.Updates == 0 ? "connected · no data yet" : $"live · {s.Chunks} chunks · {Age(nowNs - s.LastUpdateNs)}";
        }

        static string Age(long ns)
        {
            long s = ns / NsPerS;
            return s < 120 ? $"{s} s ago" : $"{s / 60} min ago";
        }
    }
}
