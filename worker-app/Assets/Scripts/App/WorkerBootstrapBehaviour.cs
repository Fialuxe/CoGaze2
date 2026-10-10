using System;
using System.IO;
using CoGaze.Clock;
using CoGaze.Core;
using CoGaze.Geometry;
using CoGaze.Logging;
using CoGaze.Worker.Cue;
using CoGaze.Worker.Cue.Rendering;
using CoGaze.Worker.Replay;
using CoGaze.Worker.Surface;
using UnityEngine;

namespace CoGaze.Worker.App
{
    /// <summary>
    /// Worker bootstrap. Today it runs the editor rehearsal of the display path: synthetic cues →
    /// CuePipeline → Ray/Circle/Frustum on a manual work surface, with poses from the main camera,
    /// cycling through the visualisations so each can be judged. The live wiring — transport (#20),
    /// capture (#21), QR surface (#23), WorkerStatus / pose.csv / clock responder (#26) — replaces the
    /// stand-ins here and nowhere else.
    /// </summary>
    public sealed class WorkerBootstrapBehaviour : MonoBehaviour
    {
        const float CycleS = 6f;
        static readonly Visualization[] Cycle = { Visualization.Circle, Visualization.Ray, Visualization.Frustum };

        [Tooltip("Plane the cues land on. Up axis is the normal.")]
        [SerializeField] ManualWorkSurfaceBehaviour surface;

        readonly StopwatchClock _clock = new StopwatchClock();
        CsvLogSink _sink;
        CuePipeline _pipeline;
        FramePoseHistory _poses;
        ReceivedCondition _condition;
        Camera _camera;
        long _frameId;
        int _cycleIndex = -1;

        void Start()
        {
            _camera = Camera.main;
            if (surface == null) surface = CreateDeskInFrontOf(_camera.transform);

            string root = Path.Combine(Application.persistentDataPath, "sessions");
            string dir = SessionFolder.Create(root, "00", DateTime.Now, new SessionManifest { AppName = "worker-rehearsal", AppVersion = Application.version });
            _sink = new CsvLogSink(dir);
            Debug.Log($"[Worker] logging to {dir}");

            _poses = new FramePoseHistory(capacity: 512);
            _condition = new ReceivedCondition();
            var renderers = new ICueRenderer[]
            {
                new GameObject("RayCue").AddComponent<RayCueRendererBehaviour>(),
                new GameObject("CircleCue").AddComponent<CircleCueRendererBehaviour>(),
                new GameObject("FrustumCue").AddComponent<FrustumCueRendererBehaviour>(),
            };
            var receiver = new SyntheticCueReceiver(_clock, () => _frameId);
            _pipeline = new CuePipeline(receiver, _condition, _poses, surface, renderers, _sink, _clock, new CueStyle(3f));
        }

        void Update()
        {
            AdvanceRehearsalCondition();
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

        static ManualWorkSurfaceBehaviour CreateDeskInFrontOf(Transform head)
        {
            var desk = GameObject.CreatePrimitive(PrimitiveType.Plane);
            desk.name = "Desk (editor)";
            desk.transform.position = head.position + head.forward * 0.6f + Vector3.down * 0.45f;
            desk.transform.localScale = new Vector3(0.12f, 1f, 0.08f);
            return desk.AddComponent<ManualWorkSurfaceBehaviour>();
        }

        void OnDestroy() => _sink?.Dispose();
    }
}
