using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CoGaze.Config
{
    /// <summary>
    /// Reads and checks app.json. Every problem is collected and returned so the checklist can show
    /// them all at once; nothing falls back to a default silently.
    /// </summary>
    public static class AppConfigLoader
    {
        public static AppConfig Load(string path, List<string> errors)
        {
            if (!File.Exists(path))
            {
                errors.Add($"app.json not found at {path}.");
                return null;
            }
            return Parse(File.ReadAllText(path), errors);
        }

        public static AppConfig Parse(string json, List<string> errors)
        {
            AppConfig c;
            try
            {
                c = JsonConvert.DeserializeObject<AppConfig>(json,
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (JsonException e)
            {
                errors.Add($"app.json: {e.Message}");
                return null;
            }
            if (c == null)
            {
                errors.Add("app.json is empty.");
                return null;
            }
            Validate(c, errors);
            return c;
        }

        static void Validate(AppConfig c, List<string> errors)
        {
            if (c.View != "2d" && c.View != "3d") errors.Add($"view must be \"2d\" or \"3d\", got \"{c.View}\".");
            if (c.CueRateHz <= 0 || c.CueRateHz > 30) errors.Add($"cue_rate_hz must be in (0, 30] (EXP-F-043), got {c.CueRateHz}.");
            if (c.StatusTimeoutS <= 0) errors.Add("status_timeout_s must be positive.");
            if (string.IsNullOrWhiteSpace(c.SessionsDir)) errors.Add("sessions_dir is empty.");
            if (string.IsNullOrWhiteSpace(c.ConditionsFile)) errors.Add("conditions_file is empty.");

            var m = c.Monitor;
            if (m == null) errors.Add("monitor is missing.");
            else if (m.WidthPx <= 0 || m.HeightPx <= 0 || m.WidthMm <= 0 || m.HeightMm <= 0 || m.ViewingDistanceMm <= 0)
                errors.Add("monitor: every size must be positive.");

            var r = c.VideoRectPx;
            if (r == null || r.Width <= 0 || r.Height <= 0) errors.Add("video_rect_px must have a positive width and height.");
            else if (m != null && (r.X < 0 || r.Y < 0 || r.X + r.Width > m.WidthPx || r.Y + r.Height > m.HeightPx))
                errors.Add("video_rect_px must lie inside the monitor.");

            if (c.Filters == null || !c.Filters.ContainsKey("default")) errors.Add("filters must define a \"default\" profile.");
            else
                foreach (var kv in c.Filters)
                    if (kv.Value.Enabled && (kv.Value.MinCutoffHz <= 0 || kv.Value.DCutoffHz <= 0 || kv.Value.Beta < 0))
                        errors.Add($"filters.{kv.Key}: cutoffs must be positive and beta non-negative.");

            if (c.Sigma == null || c.Sigma.DefaultSigmaDeg <= 0 || c.Sigma.LiveWindow < 2)
                errors.Add("sigma: default_sigma_deg must be positive and live_window at least 2.");
            if (c.Network == null || c.Network.OscListenPort <= 0 || c.Network.OscListenPort > 65535)
                errors.Add("network.osc_listen_port must be a valid port.");
            if (c.Beam != null && c.Beam.PxScale <= 0) errors.Add("beam.px_scale must be positive.");
            if (c.Fake == null || c.Fake.RateHz <= 0) errors.Add("fake.rate_hz must be positive.");
        }
    }
}
