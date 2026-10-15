using System;
using System.Collections.Generic;
using System.IO;
using CoGaze.Clock;
using CoGaze.Core;
using CoGaze.Geometry;
using CoGaze.Logging;
using CoGaze.Worker.Capture.Passthrough;
using CoGaze.Worker.Cue;
using CoGaze.Worker.Replay;
using CoGaze.Worker.Surface;
using UnityEngine;

namespace CoGaze.Worker.App
{
    /// <summary>
    /// Worker bootstrap. Live (on device, or in the editor with editorRehearsal off): WorkerLive —
    /// WebRTC link, passthrough capture, cues, condition echo, status, pose log (#20, #21, #26).
    /// Editor rehearsal: the display path: synthetic cues →
    /// CuePipeline → Ray/Circle/Frustum on a manual work surface, with poses from the main camera,
    /// cycling through the visualisations so each can be judged. The live wiring — transport (#20),
    /// capture (#21), QR surface (#23), WorkerStatus / pose.csv / clock responder (#26) — replaces the
    /// stand-ins here and nowhere else. Either way the cue renderers come from the additive Cue_* scene
    /// the condition names (CueSceneLoader); this object sits in the base scene, Worker.unity.
    /// </summary>
    public sealed class WorkerBootstrapBehaviour : MonoBehaviour
    {
        const float CycleS = 6f;
        static readonly Visualization[] Cycle = { Visualization.Circle, Visualization.Ray, Visualization.Frustum };

        [Tooltip("Plane the cues land on. Up axis is the normal.")]
        [SerializeField] ManualWorkSurfaceBehaviour surface;

        [Tooltip("In the editor, run the synthetic-cue rehearsal instead of the live link. Ignored on device.")]
        [SerializeField] bool editorRehearsal = true;
        [Tooltip("Signaling relay base URL (server/service.py or signaling.py); the role is appended.")]
        [SerializeField] string signalingUrl = "ws://192.168.0.10:8765/signal";
        [Tooltip("Optional: an existing capture component; one is added to this object when empty.")]
        [SerializeField] PassthroughCaptureBehaviour capture;

        readonly StopwatchClock _clock = new StopwatchClock();
        CsvLogSink _sink;
        CuePipeline _pipeline;
        CueSceneLoader _scenes;
        FramePoseHistory _poses;
        ReceivedCondition _condition;
        Camera _camera;
        long _frameId;
        int _cycleIndex = -1;
#if COGAZE_WEBRTC
        WorkerLive _live;
#endif

        bool Rehearsal => Application.isEditor && editorRehearsal;

        void Start()
        {
            _camera = Camera.main;
            if (surface == null) surface = CreateDeskInFrontOf(_camera.transform, visible: Rehearsal);
            if (!Rehearsal)
            {
#if COGAZE_WEBRTC
                if (capture == null) capture = gameObject.AddComponent<PassthroughCaptureBehaviour>();
                _live = new WorkerLive(gameObject, signalingUrl, _clock, surface, capture, _camera.transform);
#else
                Debug.LogError("[Worker] This build has no com.unity.webrtc, so it cannot run live. Add the package or use editor rehearsal.");
#endif
                return;
            }

            string root = Path.Combine(Application.persistentDataPath, "sessions");
            string dir = SessionFolder.Create(root, "00", DateTime.Now, new SessionManifest { AppName = "worker-rehearsal", AppVersion = Application.version });
            _sink = new CsvLogSink(dir);
            Debug.Log($"[Worker] logging to {dir}");

            _poses = new FramePoseHistory(capacity: 512);
            _condition = new ReceivedCondition();
            var renderers = new List<ICueRenderer>();
            _scenes = new CueSceneLoader(_condition, renderers, _sink, _clock);
            var receiver = new SyntheticCueReceiver(_clock, () => _frameId);
            _pipeline = new CuePipeline(receiver, _condition, _poses, surface, renderers, _sink, _clock, new CueStyle(3f));
        }

        void Update()
        {
#if COGAZE_WEBRTC
            if (_live != null)
            {
                _live.Tick(Time.deltaTime);
                return;
            }
#endif
            if (_pipeline == null) return;
            AdvanceRehearsalCondition();
            _scenes.Tick();
            _poses.Add(CameraFrameFrom(_camera, ++_frameId, _clock.NowNs));
            _pipeline.Tick(Time.deltaTime);
        }

        void AdvanceRehearsalCondition()
        {
            int i = (int)(Time.time / CycleS) % (Cycle.Length * 2);
            if (i == _cycleIndex) return;
            _cycleIndex = i;
            var vis = Cycle[i % Cycle.Length];
            bool show = i >= Cycle.Length;
            _condition.Apply(new Condition($"rehearsal-{vis}-{(show ? "sigma" : "fixed")}".ToLowerInvariant(), "replay",
                                           vis, show, SigmaSource.Fixed, 3f, "default", "rehearsal"));
        }

        /// <summary>Stand-in for passthrough capture (#21): the main camera with a Quest-like 4:3 pinhole.</summary>
        static CameraFrame CameraFrameFrom(Camera cam, long id, long tNs)
        {
            const int w = 1280, h = 960;
            float fy = h / 2f / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f);
            var t = cam.transform;
            return new CameraFrame(id, tNs, t.position.ToNumerics(), t.rotation.ToNumerics(), fy, fy, w / 2f, h / 2f, w, h);
        }

        /// <summary>A stand-in plane; drawn only in rehearsal, so the worker never sees a slab over the real desk (WRK-F-033).</summary>
        static ManualWorkSurfaceBehaviour CreateDeskInFrontOf(Transform head, bool visible)
        {
            var desk = GameObject.CreatePrimitive(PrimitiveType.Plane);
            desk.name = "Desk (editor)";
            desk.transform.position = head.position + head.forward * 0.6f + Vector3.down * 0.45f;
            desk.transform.localScale = new Vector3(0.12f, 1f, 0.08f);
            desk.GetComponent<MeshRenderer>().enabled = visible;
            return desk.AddComponent<ManualWorkSurfaceBehaviour>();
        }

        void OnDestroy()
        {
            _sink?.Dispose();
#if COGAZE_WEBRTC
            _live?.Dispose();
#endif
        }
    }
}
