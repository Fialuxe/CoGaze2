using CoGaze.Core;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>Keeps the "3D reconstruction" checklist row current; ticked every frame, updates twice a second.</summary>
    public sealed class ReconstructionHealthReporter
    {
        const long IntervalNs = 500_000_000L;

        readonly ReconstructionStatus _status;
        readonly IHealthBoard _health;
        readonly IClock _clock;
        long _nextNs;

        public ReconstructionHealthReporter(ReconstructionStatus status, IHealthBoard health, IClock clock)
        {
            _status = status;
            _health = health;
            _clock = clock;
            _nextNs = clock.NowNs;
        }

        public void Tick()
        {
            long now = _clock.NowNs;
            if (now < _nextNs) return;
            _nextNs = now + IntervalNs;
            _health.Set(ReconstructionHealth.Evaluate(_status, now));
        }
    }
}
