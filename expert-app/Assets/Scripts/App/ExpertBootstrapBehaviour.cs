using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoGaze.Clock;
using CoGaze.Config;
using CoGaze.Core;
using CoGaze.Expert.Calibration;
using CoGaze.Expert.Experiment;
using CoGaze.Expert.Pipeline;
using CoGaze.Expert.Sources.Fake;
using CoGaze.Expert.Sources.Osc;
using CoGaze.Expert.UI;
using CoGaze.Expert.View2D;
using CoGaze.Logging;
using CoGaze.Transport.Loopback;
using UnityEngine;
#if COGAZE_BEAM
using CoGaze.Expert.Sources.Beam;
#endif

namespace CoGaze.Expert.App
{
    /// <summary>
    /// The only place that chooses concrete parts and connects them. Everything below this class
    /// talks through CoGaze.Core. Rehearsal (app.json "rehearsal": true) swaps the external parts —
    /// Beam, Tobii, the worker — for fakes; all other code is the code that runs in the experiment.
    /// </summary>
    public sealed class ExpertBootstrapBehaviour : MonoBehaviour
    {
        readonly StopwatchClock _clock = new StopwatchClock();
        readonly List<IDisposable> _owned = new List<IDisposable>();
        readonly List<Action> _tickers = new List<Action>();

        ExpertScreenBehaviour _screen;
        string _configDir, _appJson, _conditionsJson;
        AppConfig _config;
        ConditionSet _conditions;

        // live once a session is open
        IMessageChannel _channel;
        TrialRunner _runner;
        WorkerStatusMonitor _worker;
        GazePipeline _pipeline;
        LoopbackTransport _loopback;
        CsvLogSink _sink;
        OscControlSender _control;
        string _sessionDir;
        long _rehearsalFrameId;
        Func<bool> _beamTracking = () => false;

        void Awake()
        {
            Application.targetFrameRate = 120;
            _screen = new GameObject("ExpertScreen").AddComponent<ExpertScreenBehaviour>();
            _screen.Build();

            _configDir = ConfigDirectory();
            var errors = new List<string>();
            _config = AppConfigLoader.Load(Path.Combine(_configDir, "app.json"), errors);
            if (_config != null)
            {
                string conditionsPath = Path.Combine(_configDir, _config.ConditionsFile);
                if (!File.Exists(conditionsPath)) errors.Add($"{_config.ConditionsFile} not found at {conditionsPath}.");
                else
                {
                    _conditionsJson = File.ReadAllText(conditionsPath);
                    _conditions = ConditionSet.Parse(_conditionsJson, _config.Filters.Keys, errors);
                }
                _appJson = File.ReadAllText(Path.Combine(_configDir, "app.json"));
            }
            if (_config != null && _config.View == "3d") errors.Add("view \"3d\" is not available yet (#47). Use \"2d\".");

            if (errors.Count > 0)
            {
                _screen.ShowFatal("Configuration problems", errors, $"Fix the files in {_configDir} and restart.");
                return;
            }
            _screen.ShowSetup(_conditions.Orders.Keys, _configDir, _config.Rehearsal, OpenSession);
        }

        /// <summary>Returns an error to show on the setup card, or null once the session is running.</summary>
        string OpenSession(string participantId)
        {
            if (string.IsNullOrEmpty(participantId)) return "Enter a participant ID.";
            if (!_conditions.Orders.TryGetValue(participantId, out var plan))
                return $"No order for \"{participantId}\" in {_config.ConditionsFile}.";

            string sessionsRoot = Path.Combine(Path.GetDirectoryName(_configDir), _config.SessionsDir);
            int resumeAt = ProgressStore.FindResumeIndex(sessionsRoot, participantId, out string resumedFrom);
            var manifest = new SessionManifest
            {
                AppName = "expert",
                AppVersion = Application.version,
                GitCommit = GitInfo.Head(Path.GetDirectoryName(_configDir)),
                AppConfigJson = _appJson,
                ConditionsJson = _conditionsJson,
                ConditionOrder = plan.Select(p => p.ConditionId).ToArray(),
            };
            try
            {
                _sessionDir = SessionFolder.Create(sessionsRoot, participantId, DateTime.Now, manifest);
            }
            catch (IOException e)
            {
                return e.Message;
            }

            _sink = new CsvLogSink(_sessionDir);
            _owned.Add(_sink);
            var health = new HealthBoard(_sink, _clock);
            _sink.Event(_clock.NowNs, EventKinds.SessionStart,
                $"participant={participantId} rehearsal={(_config.Rehearsal ? 1 : 0)} resume_index={resumeAt} resumed_from={resumedFrom}");

            var cueSender = BuildTransport(health);
            _runner = new TrialRunner(participantId, plan, _conditions.Conditions, _channel, _sink, _clock, health, _sessionDir, resumeAt);
            _runner.TrialClosed += _sink.Flush;
            _worker = new WorkerStatusMonitor(health, _clock, _config.StatusTimeoutS);

            var sources = BuildSources(health);
            var screenGeometry = new ScreenGeometry(_config.Monitor.WidthPx, _config.Monitor.WidthMm, _config.Monitor.ViewingDistanceMm);
            var calibration = new CalibrationState();
            var sigma = new ConditionSigmaPolicy(_runner,
                new FixedSigmaPolicy(_runner),
                new CalibSigmaPolicy(calibration, _config.Sigma.DefaultSigmaDeg),
                new CalibLiveSigmaPolicy(calibration, _config.Sigma.DefaultSigmaDeg, screenGeometry, _config.Sigma.LiveWindow));
            var r = _config.VideoRectPx;
            var frames = new DisplayedFrameLog();
            var projector = new View2DProjector(new VideoRect(r.X, r.Y, r.Width, r.Height), frames);
            var filters = _config.Filters.ToDictionary(kv => kv.Key,
                kv => new FilterSettings(kv.Value.Enabled, kv.Value.MinCutoffHz, kv.Value.Beta, kv.Value.DCutoffHz));
            _pipeline = new GazePipeline(sources, _runner, projector, sigma, cueSender, _sink, _clock, filters, _config.CueRateHz);

            var sourceActions = sources.ToDictionary(s => s.Name, s => ActionFor(s.Name));
            var sourceHealth = new SourceHealthReporter(_pipeline, health, _clock, _config.StatusTimeoutS, sourceActions);
            var logHealth = new LogHealthReporter(_sink, health, _clock, _sessionDir, _config.Health.LogQueueWarn, _config.Health.MinDiskGb);
            _tickers.Add(sourceHealth.Tick);
            _tickers.Add(logHealth.Tick);
            _tickers.Add(_worker.Tick);
            _tickers.Add(_runner.Tick);
            if (_config.Rehearsal) _tickers.Add(() => frames.Record(_clock.NowNs, _rehearsalFrameId++));

            if (!_config.Rehearsal)
            {
                _control = new OscControlSender(_config.Network.OscControlPorts, _clock);
                _owned.Add(_control);
                _control.Start(_sessionDir);
            }
            _channel.Send(ControlMessage.Session(Path.GetFileName(_sessionDir)));

            _screen.ShowSession(_runner, _runner, _runner, health, _clock,
                                new Rect(r.X, r.Y, r.Width, r.Height), _config.Rehearsal);
            _sink.Event(_clock.NowNs, EventKinds.ReadyToStart, $"startup_ms={Time.realtimeSinceStartup * 1000:0}");
            return null;
        }

        ICueSender BuildTransport(IHealthBoard health)
        {
            if (_config.Rehearsal)
            {
                _loopback = new LoopbackTransport(_clock);
                _channel = _loopback;
                return _loopback;
            }
            // WebRTC transport (#20) is not built yet: say so on the checklist rather than pretend.
            var none = new UnavailableTransport();
            _channel = none;
            health.Set(new HealthItem("transport", "Worker transport", HealthLevel.Error, "not built",
                "The WebRTC transport is not implemented yet (#20).", "Use rehearsal mode until it lands."));
            return none;
        }

        List<IGazeSource> BuildSources(IHealthBoard health)
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
            var processes = new ProcessHealthReporter(router.Status, health, _clock, _config.StatusTimeoutS,
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
                    var beamHealth = new BeamHealthReporter(beam, health);
                    _tickers.Add(beamHealth.Tick);
                    _beamTracking = () => beamHealth.Tracking;
                }
                else sources.Add(new QueueGazeSource("beam", router.Beam));
#else
                if (_config.Beam.Enabled)
                    health.Set(new HealthItem("beam_app", "Beam app", HealthLevel.Error, "SDK missing",
                        "Beam is enabled but this build has no Beam SDK.", "See expert-app/Assets/ThirdParty/Beam/README.md."));
                sources.Add(new QueueGazeSource("beam", router.Beam));
#endif
            }
            foreach (var name in names.Where(n => n.StartsWith("webcam:")))
                health.Set(new HealthItem("source_" + name, "Gaze · " + name, HealthLevel.Error, "no mapping",
                    "Webcam gaze needs calibration, which is not built yet (#32).", "Use beam or tobii conditions for now."));
            return sources;
        }

        static string ActionFor(string source)
        {
            switch (source)
            {
                case "beam": return "Open the Beam window and check its camera input.";
                case "tobii": return "Check the Tobii is plugged in and the bridge is running.";
                default: return "Check the estimation process is running.";
            }
        }

        void Update()
        {
            if (_runner == null) return;
            while (_channel.TryReceive(out var m))
            {
                _runner.Handle(m);
                _worker.Handle(m);
            }
            _pipeline.Tick();
            foreach (var tick in _tickers) tick();

            _screen.SetRecordingIndicator(_beamTracking());
            if (_loopback != null) PreviewCue();
        }

        /// <summary>Rehearsal: draw on the expert screen what the loopback worker received.</summary>
        void PreviewCue()
        {
            var c = _loopback.LastCue;
            bool show = _runner.HasCondition && _runner.Current.SendsGaze && _loopback.CuesReceived > 0;
            float sigma = _runner.HasCondition && _runner.Current.ShowUncertainty ? c.SigmaDeg : _config.Sigma.DefaultSigmaDeg;
            var g = new ScreenGeometry(_config.Monitor.WidthPx, _config.Monitor.WidthMm, _config.Monitor.ViewingDistanceMm);
            _screen.ShowCuePreview(show && c.Valid, c.U, c.V, sigma * g.PxPerDeg);
        }

        void OnDestroy()
        {
            if (_sink != null) _sink.Event(_clock.NowNs, EventKinds.SessionEnd, "app_exit");
            _control?.Stop(_sessionDir);
            for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();   // sink last: it was added first
        }

        static string ConfigDirectory()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "config"));
#else
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "config");
#endif
        }
    }
}
