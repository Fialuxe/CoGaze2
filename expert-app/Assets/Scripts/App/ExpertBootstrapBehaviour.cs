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
using CoGaze.Expert.Sources.Osc;
using CoGaze.Expert.UI;
using CoGaze.Expert.View2D;
using CoGaze.Expert.View3D;
using CoGaze.Expert.View3D.Model;
using CoGaze.Logging;
using CoGaze.Transport.Loopback;
using UnityEngine;
#if COGAZE_WEBRTC
using CoGaze.Transport.WebRtc;
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
#if COGAZE_WEBRTC
        ExpertLiveLink _live;
#endif

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

            var factory = new GazeSourceFactory(_config, _conditions, _clock, health, _owned, _tickers);
            var sources = factory.Build();
            _beamTracking = factory.BeamTracking;
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

            var sourceActions = sources.ToDictionary(s => s.Name, s => GazeSourceFactory.ActionFor(s.Name));
            var sourceHealth = new SourceHealthReporter(_pipeline, health, _clock, _config.StatusTimeoutS, sourceActions);
            var logHealth = new LogHealthReporter(_sink, health, _clock, _sessionDir, _config.Health.LogQueueWarn, _config.Health.MinDiskGb);
            _tickers.Add(sourceHealth.Tick);
            _tickers.Add(logHealth.Tick);
            _tickers.Add(_worker.Tick);
            _tickers.Add(_runner.Tick);
            if (_config.Rehearsal) _tickers.Add(() => frames.Record(_clock.NowNs, _rehearsalFrameId++));
#if COGAZE_WEBRTC
            if (_live != null) _tickers.Add(_live.Tick);
#endif

            if (!_config.Rehearsal)
            {
                _control = new OscControlSender(_config.Network.OscControlPorts, _clock);
                _owned.Add(_control);
                _control.Start(_sessionDir);
            }
            _channel.Send(ControlMessage.Session(Path.GetFileName(_sessionDir)));

            _screen.ShowSession(_runner, _runner, _runner, health, _clock,
                                new Rect(r.X, r.Y, r.Width, r.Height), _config.Rehearsal, _config.OperatorHintInTrial);
#if COGAZE_WEBRTC
            _live?.Attach(_screen, projector, frames);
#endif
            BuildView3D(health, new Rect(r.X, r.Y, r.Width, r.Height));
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
#if COGAZE_WEBRTC
            var r = _config.VideoRectPx;
            _live = new ExpertLiveLink(gameObject, _config.Network.SignalingUrl, _clock, _sink, health,
                                       new VideoRect(r.X, r.Y, r.Width, r.Height));
            _channel = _live.Link;
            return _live.Link;
#else
            // Built without com.unity.webrtc: say so on the checklist rather than pretend.
            var none = new UnavailableTransport();
            _channel = none;
            health.Set(new HealthItem("transport", "Worker transport", HealthLevel.Error, "no WebRTC",
                "This build has no com.unity.webrtc package, so there is no link to the worker.",
                "Add com.unity.webrtc 3.0.0 to Packages/manifest.json, or use rehearsal mode."));
            return none;
#endif
        }

        /// <summary>
        /// The 3D view (#47): the reconstruction rendered into the video stage, switched with the 2D
        /// video from the top bar or V. Gaze still goes through View2DProjector; 3D gaze is M4 (#47).
        /// </summary>
        void BuildView3D(IHealthBoard health, Rect stagePx)
        {
            var rc = _config.Reconstruction ?? new ReconstructionConfig();
            string dir = string.IsNullOrWhiteSpace(rc.StagingDir) ? ""
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_configDir), rc.StagingDir));
            var view = new GameObject("View3D").AddComponent<View3DBehaviour>();
            view.Build(new ReconstructionSource((rc.Url ?? "").Trim(), dir, rc.ReplayIntervalS), _clock, health, stagePx);
            view.SetPanelFilter(_screen.IsPointerOverPanel);
            _screen.ShowViewSwitch(_config.View == "3d", view.SetShown, () => view.StageLabel);
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
