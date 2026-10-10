using System;
using System.Collections;
using CoGaze.Core;
using CoGaze.Geometry;
using Meta.XR;
using UnityEngine;
using UnityEngine.Android;

namespace CoGaze.Worker.Capture.Passthrough
{
    /// <summary>
    /// The worker's left passthrough camera as video frames (WRK-F-010, 011): for every new camera
    /// image, the texture plus a CameraFrame with a fresh video_frame_id, the camera pose at the
    /// image's timestamp (Unity world) and OpenCV intrinsics of the delivered image, derived as in
    /// EnvLine with CameraGeometry (measured to 0.0002 px). Runs at end of frame, when the camera
    /// texture holds the image its pose belongs to (see KeyframeSource). Camera pixels do not come
    /// over Link (colour bars only): image content needs an on-device build; poses and intrinsics are real.
    /// </summary>
    public sealed class PassthroughCaptureBehaviour : MonoBehaviour
    {
        const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        [Tooltip("Reuse a camera another component already owns; created on this object when empty.")]
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] Vector2Int requestedResolution = new Vector2Int(1280, 960);

        IClock _clock;
        Func<long> _nextId;
        Action<Texture, CameraFrame> _onFrame;

        /// <summary>Why no frames arrive, for the log; null while capturing.</summary>
        public string Problem { get; private set; } = "not started";
        public long FramesCaptured { get; private set; }

        public void Begin(IClock clock, Func<long> nextId, Action<Texture, CameraFrame> onFrame)
        {
            _clock = clock;
            _nextId = nextId;
            _onFrame = onFrame;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission);
                float until = Time.time + 10f;
                yield return new WaitUntil(() => Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) || Time.time > until);
            }
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) || !PassthroughCameraAccess.IsSupported)
            {
                Fail("Passthrough camera not available (permission refused or headset unsupported).");
                yield break;
            }

            if (cameraAccess == null)
            {
                // The component claims its camera in OnEnable, so configure it while inactive.
                var go = new GameObject("PassthroughCamera_Left");
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                cameraAccess = go.AddComponent<PassthroughCameraAccess>();
                cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
                cameraAccess.RequestedResolution = requestedResolution;
                go.SetActive(true);
            }

            float deadline = Time.time + 10f;
            yield return new WaitUntil(() => cameraAccess.IsPlaying || Time.time > deadline);
            if (!cameraAccess.IsPlaying)
            {
                Fail("Passthrough camera did not start within 10 s.");
                yield break;
            }
            Problem = null;
            Debug.Log($"[Capture] {cameraAccess.CurrentResolution.x}x{cameraAccess.CurrentResolution.y}");

            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (!cameraAccess.IsUpdatedThisFrame) continue;
                FramesCaptured++;
                _onFrame(cameraAccess.GetTexture(), Describe(cameraAccess, _nextId(), CaptureTimeNs(cameraAccess.Timestamp)));
            }
        }

        /// <summary>The image's timestamp on this app's clock: now minus the image's age.</summary>
        long CaptureTimeNs(DateTime timestampUtc)
        {
            long ageTicks = Math.Max(0L, (DateTime.UtcNow - timestampUtc).Ticks);
            return _clock.NowNs - ageTicks * 100L;
        }

        static CameraFrame Describe(PassthroughCameraAccess pca, long id, long tCaptureNs)
        {
            var p = pca.Intrinsics;
            var k = CameraGeometry.DeriveIntrinsics(p.FocalLength, p.PrincipalPoint, p.SensorResolution, pca.CurrentResolution);
            var pose = pca.GetCameraPose();
            return new CameraFrame(id, tCaptureNs, pose.position.ToNumerics(), pose.rotation.ToNumerics(),
                                   k.fx, k.fy, k.cx, k.cy, k.width, k.height);
        }

        void Fail(string problem)
        {
            Problem = problem;
            Debug.LogError($"[Capture] {problem}");
        }
    }
}
