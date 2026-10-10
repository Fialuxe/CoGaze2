using System;
using CoGaze.Core;
using CoGaze.Expert.View3D.Model;
using CoGaze.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoGaze.Expert.View3D
{
    /// <summary>
    /// The 3D view of the worker's space: the reconstruction, received by <see cref="MeshReceiver"/>,
    /// seen through a camera whose pixel rect is the video stage, so the rest of the screen stays as
    /// it is. The expert steers it freely (<see cref="OrbitInput"/>, <see cref="OrbitCamera"/>),
    /// independently of where the worker faces. Created and wired by the App bootstrap.
    ///
    /// For gaze in 3D (M4): <see cref="ViewCamera"/> is the expert's virtual camera, and every chunk
    /// carries a MeshCollider, so a screen point becomes a mesh hit with
    /// ViewCamera.ScreenPointToRay + Physics.Raycast. The hit is in Unity's frame; to send it to the
    /// worker convert back with <see cref="MeshReceiver.ToUnityPoint"/> (its own inverse).
    /// </summary>
    public sealed class View3DBehaviour : MonoBehaviour
    {
        const float VerticalFovDeg = 60f;
        static readonly Color StageColour = new Color32(0x05, 0x07, 0x0A, 0xFF);   // .cg-stage background

        ReconstructionStatus _status;
        ReconstructionHealthReporter _reporter;
        ChunkMeshStore _store;
        MeshReceiver _receiver;
        OrbitCamera _orbit;
        readonly OrbitInput _input = new OrbitInput();
        IClock _clock;
        Rect _stageTopLeftPx;
        Func<Vector2, bool> _overPanel = _ => false;
        (Vector3 position, Vector3 forward)? _workerView;
        bool _framedOnce;

        public Camera ViewCamera { get; private set; }
        public bool Shown { get; private set; }
        public ReconstructionStatus Status => _status;

        /// <param name="stageTopLeftPx">app.json video_rect_px, top-left origin as the UI lays it out.</param>
        public void Build(ReconstructionSource source, IClock clock, IHealthBoard health, Rect stageTopLeftPx)
        {
            _clock = clock;
            _stageTopLeftPx = stageTopLeftPx;
            _status = new ReconstructionStatus(source);
            _reporter = new ReconstructionHealthReporter(_status, health, clock);

            var cameraObject = new GameObject("View3D Camera");
            cameraObject.transform.SetParent(transform, false);
            ViewCamera = cameraObject.AddComponent<Camera>();
            ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor = StageColour;
            ViewCamera.fieldOfView = VerticalFovDeg;
            ViewCamera.nearClipPlane = 0.02f;
            ViewCamera.farClipPlane = 100f;
            ViewCamera.depth = 10f;                       // after the full-screen main camera
            ViewCamera.enabled = false;

            // Start behind the worker's tracking origin, looking ahead and down at where work happens;
            // the first data reframes the view unless the expert has already steered.
            _orbit = new OrbitCamera(new OrbitPose(new System.Numerics.Vector3(0f, -0.5f, 0.8f), 0f, 30f, 1.6f));

            _store = new ChunkMeshStore(transform, VertexColourMaterial());
            _receiver = new MeshReceiver(source, _store, _status, clock);
            _receiver.Start();
        }

        /// <summary>Where the UI covers the stage, so drags there are left to the UI (screen px, bottom-left).</summary>
        public void SetPanelFilter(Func<Vector2, bool> overPanel) => _overPanel = overPanel ?? (_ => false);

        public void SetShown(bool shown)
        {
            Shown = shown;
            if (ViewCamera != null) ViewCamera.enabled = shown;
        }

        /// <summary>What the stage label says in 3D mode.</summary>
        public string StageLabel => _status == null ? "" : ReconstructionHealth.StageLabel(_status, _clock.NowNs);

        /// <summary>
        /// The worker's head pose in the server's frame (OpenCV world_T_camera position and +Z axis),
        /// for Home. Nothing on the wire carries it yet; until something calls this, Home returns to
        /// the start view.
        /// </summary>
        public void SetWorkerViewpoint(Vector3 positionServer, Vector3 forwardServer) =>
            _workerView = (MeshReceiver.ToUnityPoint(positionServer), MeshReceiver.ToUnityPoint(forwardServer));

        void Update()
        {
            if (_status == null) return;
            _reporter.Tick();
            if (!Shown) return;

            var viewport = StageViewport.ToBottomLeft(_stageTopLeftPx.x, _stageTopLeftPx.y, _stageTopLeftPx.width,
                                                      _stageTopLeftPx.height, Screen.height);
            ViewCamera.pixelRect = new Rect(viewport.x, viewport.y, viewport.w, viewport.h);

            _input.Poll(_orbit, viewport, VerticalFovDeg, _overPanel, out bool frame, out bool home);
            if (frame || !_framedOnce && !_orbit.UserMoved && _store.Count > 0) FrameAll();
            if (home) GoHome();

            _orbit.Tick(Time.unscaledDeltaTime);
            var pose = _orbit.Current;
            ViewCamera.transform.SetPositionAndRotation(pose.Position.ToUnity(), Quaternion.Euler(pose.PitchDeg, pose.YawDeg, 0f));
        }

        void FrameAll()
        {
            if (!_store.TryGetBounds(out var b)) return;
            _orbit.Frame(b.min.ToNumerics(), b.max.ToNumerics(), VerticalFovDeg, ViewCamera.aspect);
            _framedOnce = true;
        }

        void GoHome()
        {
            if (_workerView.HasValue)
                _orbit.LookFrom(_workerView.Value.position.ToNumerics(), _workerView.Value.forward.ToNumerics(), 0.6f);
            else
                _orbit.LookFrom(System.Numerics.Vector3.Zero, new System.Numerics.Vector3(0f, -0.5f, 1f), 0.8f);
        }

        static Material VertexColourMaterial()
        {
            // VertexColorUnlit is a URP shader; without a URP asset nothing would draw, and that must not be silent.
            if (GraphicsSettings.currentRenderPipeline == null)
                Debug.LogError("[View3D] No URP pipeline asset is assigned, so the reconstruction will not render. " +
                               "Run CoGaze > Set Up Rendering in the editor.");
            var shader = Shader.Find("CoGaze2/VertexColorUnlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader);
        }

        void OnDestroy()
        {
            _receiver?.Dispose();
            _store?.Clear();
        }
    }
}
