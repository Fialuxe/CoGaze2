#if COGAZE_WEBRTC
using System;
using System.Globalization;
using System.IO;
using CoGaze.Core;
using CoGaze.Geometry;
using CoGaze.Logging;
using CoGaze.Transport.WebRtc;
using CoGaze.Worker.Capture;
using CoGaze.Worker.Capture.Passthrough;
using CoGaze.Worker.Cue;
using CoGaze.Worker.Cue.Rendering;
using CoGaze.Worker.Session;
using UnityEngine;
using UnityEngine.XR;

namespace CoGaze.Worker.App
{
    /// <summary>
    /// The live worker (#21, #26): passthrough frames with ids into the WebRTC video and pose history,
    /// cues from the expert onto the work surface, the condition and trial as the expert sends them,
    /// clock pongs, WorkerStatus at 1 Hz and pose.csv at 30 Hz. Logs go to
    /// persistentDataPath/sessions/&lt;the expert's session name&gt;/ once the expert names it;
    /// rows before that are dropped (counted in ForwardingLogSink.Dropped).
    /// </summary>
    sealed class WorkerLive : IDisposable
    {
        const int VideoWidthPx = 1280, VideoHeightPx = 960;   // TBD-10

        readonly IClock _clock;
        readonly IWorkSurface _surface;
        readonly Transform _head;
        readonly ForwardingLogSink _log = new ForwardingLogSink();
        readonly StripFrameTagWriter _writer;
        readonly WebRtcLinkBehaviour _link;
        readonly LinkMonitor _monitor;
        readonly FrameRecorder _frames;
        readonly CuePipeline _pipeline;
        readonly ControlResponder _responder;
        readonly StatusReporter _status;
        readonly PoseLogger _pose;
        CsvLogSink _csv;

        public WorkerLive(GameObject host, string signalingUrl, IClock clock, IWorkSurface surface,
                          PassthroughCaptureBehaviour capture, Transform head)
        {
            _clock = clock;
            _surface = surface;
            _head = head;
            _writer = new StripFrameTagWriter(VideoWidthPx, VideoHeightPx);
            _link = host.AddComponent<WebRtcLinkBehaviour>();
            _link.Begin(LinkRole.Worker, signalingUrl, clock, _writer.Output);
            _monitor = new LinkMonitor(_log, null, LinkRole.Worker, _link.RelayUrl);

            var poses = new FramePoseHistory(capacity: 512);   // 5 s retention at up to ~100 fps
            _frames = new FrameRecorder(poses, _log);
            capture.Begin(clock, _frames.NextId, (image, frame) =>
            {
                _writer.Write(image, frame.VideoFrameId);
                _frames.Record(frame);
            });

            var condition = new ReceivedCondition();
            var renderers = new ICueRenderer[]
            {
                new GameObject("RayCue").AddComponent<RayCueRendererBehaviour>(),
                new GameObject("CircleCue").AddComponent<CircleCueRendererBehaviour>(),
                new GameObject("FrustumCue").AddComponent<FrustumCueRendererBehaviour>(),
            };
            _pipeline = new CuePipeline(_link, condition, poses, surface, renderers, _log, clock, new CueStyle(3f));
            _responder = new ControlResponder(_link, clock, _log, c => condition.Apply(c), t => condition.Apply(t), OpenSession);
            _status = new StatusReporter(_link, clock);
            _pose = new PoseLogger(_log);
        }

        public void Tick(float deltaS)
        {
            long now = _clock.NowNs;
            _monitor.Update(_link.State, now);
            _responder.Drain();
            _pipeline.Tick(deltaS);
            _status.Tick(_surface.State, _csv != null);
            _pose.Tick(now, new TrackedPose(true, _head.position.ToNumerics(), _head.rotation.ToNumerics()),
                       Hand(XRNode.LeftHand), Hand(XRNode.RightHand));
        }

        /// <summary>Hand pose from the XR input system; untracked (empty in pose.csv) when the runtime reports none.</summary>
        static TrackedPose Hand(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid ||
                !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion q))
                return TrackedPose.None;
            return new TrackedPose(true, p.ToNumerics(), q.ToNumerics());
        }

        /// <summary>Session message: the same folder name as the expert's (SRS 3.4). Reused if it already exists.</summary>
        void OpenSession(string name)
        {
            if (_csv != null || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
            string dir = Path.Combine(Application.persistentDataPath, "sessions", name);
            Directory.CreateDirectory(dir);
            string manifest = Path.Combine(dir, "session.json");
            if (!File.Exists(manifest))
            {
                var m = new SessionManifest { AppName = "worker", AppVersion = Application.version };
                string participant = name.Split('_')[0].TrimStart('P');
                File.WriteAllText(manifest, m.ToJson(participant, DateTime.Now).ToString());
            }
            _csv = new CsvLogSink(dir);
            _log.Attach(_csv);
            _csv.Event(_clock.NowNs, "worker_log_open",
                       string.Format(CultureInfo.InvariantCulture, "dropped_before={0} frames_so_far={1}", _log.Dropped, _frames.LastId));
            Debug.Log($"[Worker] logging to {dir}");
        }

        public void Dispose()
        {
            _csv?.Dispose();
            _writer.Dispose();
        }
    }
}
#endif
