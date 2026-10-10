using CoGaze.Core;

namespace CoGaze.Expert.Experiment
{
    /// <summary>
    /// Worker link, alignment and recording on the checklist, from the 1 Hz WorkerStatus (EXP-F-001).
    /// Silence past the timeout means the link is down (EXP-F-003).
    /// </summary>
    public sealed class WorkerStatusMonitor
    {
        readonly IHealthBoard _health;
        readonly IClock _clock;
        readonly long _timeoutNs;
        long _lastNs = -1;
        WorkerStatus _last;

        public WorkerStatusMonitor(IHealthBoard health, IClock clock, float timeoutS)
        {
            _health = health;
            _clock = clock;
            _timeoutNs = (long)(timeoutS * 1e9);
        }

        public void Handle(in ControlMessage m)
        {
            if (m.Kind != MessageKind.WorkerStatus) return;
            _last = m.Status;
            _lastNs = _clock.NowNs;
        }

        public void Tick()
        {
            long now = _clock.NowNs;
            if (_lastNs < 0 || now - _lastNs > _timeoutNs)
            {
                string since = _lastNs < 0 ? "never connected" : $"silent {(now - _lastNs) / 1e9:0} s";
                _health.Set(new HealthItem("worker_link", "Worker link", HealthLevel.Error, since,
                    "No status from the worker app.", "Check the Quest is on the same network and the app is running."));
                _health.Set(new HealthItem("worker_alignment", "Work surface", HealthLevel.Unknown, "—"));
                _health.Set(new HealthItem("worker_recording", "Worker recording", HealthLevel.Unknown, "—"));
                return;
            }
            _health.Set(new HealthItem("worker_link", "Worker link", HealthLevel.Ok, $"{_last.FrameRateHz:0} fps"));
            _health.Set(_last.Alignment == AlignmentState.Aligned
                ? new HealthItem("worker_alignment", "Work surface", HealthLevel.Ok, "aligned")
                : new HealthItem("worker_alignment", "Work surface", HealthLevel.Error,
                    _last.Alignment == AlignmentState.TrackingLost ? "tracking lost" : "not aligned",
                    "The work surface is not aligned on the Quest.", "Ask the worker to look at the QR marker on the desk."));
            _health.Set(_last.Recording
                ? new HealthItem("worker_recording", "Worker recording", HealthLevel.Ok, "recording")
                : new HealthItem("worker_recording", "Worker recording", HealthLevel.Error, "stopped",
                    "The worker app is not recording.", "Restart the session on the worker app."));
        }
    }
}
