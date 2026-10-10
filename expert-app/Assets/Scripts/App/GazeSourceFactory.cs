using System;
using System.Collections.Generic;
using System.Linq;
using CoGaze.Config;
using CoGaze.Core;
using CoGaze.Expert.Experiment;
using CoGaze.Expert.Sources.Fake;
using CoGaze.Expert.Sources.Osc;
#if COGAZE_BEAM
using CoGaze.Expert.Sources.Beam;
#endif

namespace CoGaze.Expert.App
{
    /// <summary>
    /// Picks the gaze sources for a session: fakes in rehearsal, otherwise OSC (Tobii, Beam via OSC)
    /// and the Beam SDK when it is compiled in. Part of the bootstrap, split out to keep it readable.
    /// </summary>
    sealed class GazeSourceFactory
    {
        readonly AppConfig _config;
        readonly ConditionSet _conditions;
        readonly IClock _clock;
        readonly IHealthBoard _health;
        readonly List<IDisposable> _owned;
        readonly List<Action> _tickers;

        public GazeSourceFactory(AppConfig config, ConditionSet conditions, IClock clock, IHealthBoard health,
                                 List<IDisposable> owned, List<Action> tickers)
        {
            _config = config;
            _conditions = conditions;
            _clock = clock;
            _health = health;
            _owned = owned;
            _tickers = tickers;
        }

        /// <summary>True while Beam reports tracking — drives the licence-required indicator.</summary>
        public Func<bool> BeamTracking { get; private set; } = () => false;

        public List<IGazeSource> Build()
        {
            var names = _conditions.Conditions.Values.Select(c => c.DisplaySource)
                .Where(s => s != Condition.NoSource && s != "replay").Distinct().OrderBy(s => s).ToList();
            var sources = new List<IGazeSource>();
            var r = _config.VideoRectPx;
            var f = _config.Fake;

            if (_config.Rehearsal)
            {
                int seed = f.Seed;
                foreach (var name in names)
                    sources.Add(new FakeGazeSource(name, _clock, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width * 0.38f,
                        r.Height * 0.38f, f.RateHz, seed++, name == "tobii" ? f.TobiiOffsetPx : 0f, 0.02));
                return sources;
            }

            var router = new OscRouter(_clock, _config.Beam.PxScale);
            _owned.Add(new OscUdpListener(_config.Network.OscListenPort, router));
            var processes = new ProcessHealthReporter(router.Status, _health, _clock, _config.StatusTimeoutS,
                ("rec", "Recording process", "Start the recording process (python rec)."),
                ("gze", "Gaze estimation", "Start the estimation process (python gze)."));
            _tickers.Add(processes.Tick);
            if (names.Contains("tobii")) sources.Add(new QueueGazeSource("tobii", router.Tobii));

            if (names.Contains("beam"))
            {
#if COGAZE_BEAM
                if (_config.Beam.Enabled)
                {
                    var beam = new BeamGazeSource(_clock, _config.Monitor.WidthPx, _config.Monitor.HeightPx, _config.Beam.PxScale);
                    _owned.Add(beam);
                    sources.Add(beam);
                    var beamHealth = new BeamHealthReporter(beam, _health);
                    _tickers.Add(beamHealth.Tick);
                    BeamTracking = () => beamHealth.Tracking;
                }
                else sources.Add(new QueueGazeSource("beam", router.Beam));
#else
                if (_config.Beam.Enabled)
                    _health.Set(new HealthItem("beam_app", "Beam app", HealthLevel.Error, "SDK missing",
                        "Beam is enabled but this build has no Beam SDK.", "See expert-app/Assets/ThirdParty/Beam/README.md."));
                sources.Add(new QueueGazeSource("beam", router.Beam));
#endif
            }
            foreach (var name in names.Where(n => n.StartsWith("webcam:")))
                _health.Set(new HealthItem("source_" + name, "Gaze · " + name, HealthLevel.Error, "no mapping",
                    "Webcam gaze needs calibration, which is not built yet (#32).", "Use beam or tobii conditions for now."));
            return sources;
        }

        public static string ActionFor(string source)
        {
            switch (source)
            {
                case "beam": return "Open the Beam window and check its camera input.";
                case "tobii": return "Check the Tobii is plugged in and the bridge is running.";
                default: return "Check the estimation process is running.";
            }
        }
    }
}
