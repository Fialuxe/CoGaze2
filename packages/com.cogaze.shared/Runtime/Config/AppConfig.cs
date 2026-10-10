using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoGaze.Config
{
    /// <summary>
    /// config/app.json — values developers change (COM-F-009). Field names match the JSON so the
    /// file can be read side by side with this class. Loaded once, then treated as read-only.
    /// </summary>
    public sealed class AppConfig
    {
        [JsonProperty("rehearsal")] public bool Rehearsal;
        [JsonProperty("sessions_dir")] public string SessionsDir = "sessions";
        [JsonProperty("conditions_file")] public string ConditionsFile = "conditions.json";
        [JsonProperty("view")] public string View = "2d";
        [JsonProperty("cue_rate_hz")] public float CueRateHz = 30f;
        [JsonProperty("status_timeout_s")] public float StatusTimeoutS = 3f;
        [JsonProperty("show_own_gaze")] public bool ShowOwnGaze;
        [JsonProperty("network")] public NetworkConfig Network = new NetworkConfig();
        [JsonProperty("monitor")] public MonitorConfig Monitor = new MonitorConfig();
        [JsonProperty("video_rect_px")] public RectConfig VideoRectPx = new RectConfig();
        [JsonProperty("filters")] public Dictionary<string, FilterConfig> Filters = new Dictionary<string, FilterConfig>();
        [JsonProperty("sigma")] public SigmaConfig Sigma = new SigmaConfig();
        [JsonProperty("health")] public HealthConfig Health = new HealthConfig();
        [JsonProperty("beam")] public BeamConfig Beam = new BeamConfig();
        [JsonProperty("fake")] public FakeConfig Fake = new FakeConfig();
    }

    public sealed class NetworkConfig
    {
        [JsonProperty("osc_listen_port")] public int OscListenPort = 9000;
        [JsonProperty("osc_control_ports")] public int[] OscControlPorts = { 9001, 9002 };
        [JsonProperty("signaling_url")] public string SignalingUrl = "";
    }

    /// <summary>Expert monitor geometry (TBD-03). Used for px to degree conversion.</summary>
    public sealed class MonitorConfig
    {
        [JsonProperty("width_px")] public int WidthPx = 1920;
        [JsonProperty("height_px")] public int HeightPx = 1080;
        [JsonProperty("width_mm")] public float WidthMm = 527f;
        [JsonProperty("height_mm")] public float HeightMm = 296f;
        [JsonProperty("viewing_distance_mm")] public float ViewingDistanceMm = 650f;
    }

    public sealed class RectConfig
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("width")] public float Width;
        [JsonProperty("height")] public float Height;
    }

    /// <summary>One-Euro filter profile (EXP-F-041). Conditions refer to profiles by name.</summary>
    public sealed class FilterConfig
    {
        [JsonProperty("enabled")] public bool Enabled = true;
        [JsonProperty("min_cutoff_hz")] public float MinCutoffHz = 1.0f;
        [JsonProperty("beta")] public float Beta = 0.007f;
        [JsonProperty("d_cutoff_hz")] public float DCutoffHz = 1.0f;
    }

    public sealed class SigmaConfig
    {
        /// <summary>Drawn size when show_uncertainty is off, and sigma before calibration exists.</summary>
        [JsonProperty("default_sigma_deg")] public float DefaultSigmaDeg = 3f;
        /// <summary>calib_live: samples in the dispersion window (provisional, TBD-06).</summary>
        [JsonProperty("live_window")] public int LiveWindow = 30;
    }

    public sealed class HealthConfig
    {
        [JsonProperty("log_queue_warn")] public int LogQueueWarn = 2000;
        [JsonProperty("min_disk_gb")] public float MinDiskGb = 10f;
    }

    public sealed class BeamConfig
    {
        [JsonProperty("enabled")] public bool Enabled;
        /// <summary>Beam reports OS logical pixels; multiply by this to get physical px (Windows display scaling).</summary>
        [JsonProperty("px_scale")] public float PxScale = 1f;
    }

    /// <summary>Rehearsal fakes.</summary>
    public sealed class FakeConfig
    {
        [JsonProperty("rate_hz")] public float RateHz = 30f;
        [JsonProperty("tobii_offset_px")] public float TobiiOffsetPx = 40f;
        [JsonProperty("seed")] public int Seed = 1;
    }
}
