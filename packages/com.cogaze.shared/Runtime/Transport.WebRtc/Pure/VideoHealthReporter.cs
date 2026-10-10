using CoGaze.Core;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Expert checklist row for the worker video: arriving at all, its frame rate, and whether the
    /// video_frame_id of each shown frame could be read (TBD-04). Without ids gaze still goes out but
    /// the worker must fall back to its latest pose, which the cue log marks.
    /// </summary>
    public sealed class VideoHealthReporter
    {
        public const string HealthKey = "video";
        const string Label = "Worker video";
        const long WindowNs = 1_000_000_000L;

        readonly IHealthBoard _health;
        long _windowStartNs = long.MinValue;
        long _decoded0, _failed0, _frames0;

        public VideoHealthReporter(IHealthBoard health)
        {
            _health = health;
        }

        /// <summary>
        /// Call every frame with cumulative counters: strips read, strips unreadable, distinct frames shown.
        /// The row is updated once a second.
        /// </summary>
        public void Tick(bool haveVideo, long decoded, long failed, long frames, long nowNs)
        {
            if (_windowStartNs == long.MinValue) Reset(decoded, failed, frames, nowNs);
            if (nowNs - _windowStartNs < WindowNs) return;

            double s = (nowNs - _windowStartNs) / 1e9;
            long ok = decoded - _decoded0, bad = failed - _failed0;
            double fps = (frames - _frames0) / s;
            Reset(decoded, failed, frames, nowNs);

            if (!haveVideo)
                _health.Set(new HealthItem(HealthKey, Label, HealthLevel.Error, "no video",
                    "No video is arriving from the worker.", "Check the link row; on the Quest, allow camera access."));
            else if (ok == 0)
                _health.Set(new HealthItem(HealthKey, Label, HealthLevel.Error, $"{fps:0} fps, ids unreadable",
                    "The frame-id strip under the video cannot be read, so gaze is not tied to frames.",
                    "Check the worker build matches this one; see docs/decisions/TBD-04.md."));
            else if (bad > ok / 10)
                _health.Set(new HealthItem(HealthKey, Label, HealthLevel.Warning, $"{fps:0} fps, ids {100 * ok / (ok + bad)}%",
                    "Some frame ids are unreadable (heavy compression?).", "Raise the video bitrate or reduce motion."));
            else
                _health.Set(new HealthItem(HealthKey, Label, HealthLevel.Ok, $"{fps:0} fps"));
        }

        void Reset(long decoded, long failed, long frames, long nowNs)
        {
            _decoded0 = decoded;
            _failed0 = failed;
            _frames0 = frames;
            _windowStartNs = nowNs;
        }
    }
}
