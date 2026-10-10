using CoGaze.Core;

namespace CoGaze.Expert.Sources.Beam
{
    /// <summary>Beam's own reception status on the checklist, separate from the sample-rate row.</summary>
    public sealed class BeamHealthReporter
    {
        readonly BeamGazeSource _beam;
        readonly IHealthBoard _health;

        public BeamHealthReporter(BeamGazeSource beam, IHealthBoard health)
        {
            _beam = beam;
            _health = health;
        }

        public bool Tracking { get; private set; }

        public void Tick()
        {
            Tracking = _beam.IsReceiving;
            _health.Set(Tracking
                ? new HealthItem("beam_app", "Beam app", HealthLevel.Ok, "tracking")
                : new HealthItem("beam_app", "Beam app", HealthLevel.Error, "not tracking",
                    "Beam is not sending tracking data.", "Open the Beam window and check its camera input."));
        }
    }
}
