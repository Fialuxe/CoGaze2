namespace CoGaze.Core
{
    /// <summary>
    /// Table names and column headers of every CSV, defined once and shared by the writers and the
    /// analysis scripts. Units are in the names. Changing a header changes the file format: bump Version.
    /// </summary>
    public static class LogSchema
    {
        public const int Version = 1;

        public const string Display = "display";
        public const string Events = "events";
        public const string ClockSync = "clock_sync";
        public const string Calibration = "calibration";
        public const string Cue = "cue";
        public const string Pose = "pose";
        public const string Frames = "frames";

        public static readonly string[] Tables = { Display, Events, ClockSync, Calibration, Cue, Pose, Frames };

        static readonly string[] DisplayColumns =
        {
            "t_ns", "condition_id", "trial_index", "display_source", "received_source", "t_capture_ns", "valid",
            "mode", "raw_x_px", "raw_y_px", "filt_x_px", "filt_y_px", "u", "v", "video_frame_id",
            "show_uncertainty", "sigma_source", "sigma_deg", "sent", "sent_source", "seq",
        };

        static readonly string[] EventsColumns = { "t_ns", "kind", "detail" };

        static readonly string[] ClockSyncColumns = { "t_ns", "seq", "rtt_ns", "offset_ns" };

        static readonly string[] CalibrationColumns =
        {
            "t_ns", "point_id", "point_kind", "head_prompt", "x_px", "y_px", "t_show_ns", "t_hide_ns",
        };

        static readonly string[] CueColumns =
        {
            "t_ns", "seq", "source", "valid", "pose_fallback", "hit", "hit_x_m", "hit_y_m", "hit_z_m",
            "radius_m", "half_angle_deg", "visualization", "show_uncertainty", "sigma_deg",
        };

        static readonly string[] PoseColumns =
        {
            "t_ns", "head_x_m", "head_y_m", "head_z_m", "head_qx", "head_qy", "head_qz", "head_qw",
            "lhand_x_m", "lhand_y_m", "lhand_z_m", "lhand_qx", "lhand_qy", "lhand_qz", "lhand_qw",
            "rhand_x_m", "rhand_y_m", "rhand_z_m", "rhand_qx", "rhand_qy", "rhand_qz", "rhand_qw",
        };

        static readonly string[] FramesColumns =
        {
            "video_frame_id", "t_capture_ns", "x_m", "y_m", "z_m", "qx", "qy", "qz", "qw",
            "fx_px", "fy_px", "cx_px", "cy_px", "width_px", "height_px",
        };

        /// <summary>Header for a table; null for an unknown table. Returns a copy so callers cannot alter the schema.</summary>
        public static string[] ColumnsOf(string table)
        {
            switch (table)
            {
                case Display: return (string[])DisplayColumns.Clone();
                case Events: return (string[])EventsColumns.Clone();
                case ClockSync: return (string[])ClockSyncColumns.Clone();
                case Calibration: return (string[])CalibrationColumns.Clone();
                case Cue: return (string[])CueColumns.Clone();
                case Pose: return (string[])PoseColumns.Clone();
                case Frames: return (string[])FramesColumns.Clone();
                default: return null;
            }
        }
    }
}
