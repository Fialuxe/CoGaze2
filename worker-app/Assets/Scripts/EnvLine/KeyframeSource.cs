using System;
using System.Collections;
using Meta.XR;
using CoGaze.Geometry;
using UnityEngine;
using UnityEngine.Android;

namespace CoGaze2.EnvLine
{
    /// <summary>A stereo pair with the camera parameters needed to reconstruct from it.</summary>
    public struct Keyframe
    {
        public int index;
        public int epoch;
        public long leftTimestampTicks, rightTimestampTicks;
        public int width, height;

        /// Poses in the server's convention: right-handed, Y-down, world_T_camera.
        public Pose leftPoseCv, rightPoseCv;
        public CameraGeometry.Intrinsics leftK, rightK;

        /// Tightly packed RGB, top row first (OpenCV order), width * height * 3 bytes.
        public byte[] leftRgb, rightRgb;
    }

    /// <summary>
    /// Produces stereo keyframes from the headset's passthrough cameras.
    ///
    /// Keyframes are selected on pose change rather than emitted every frame: reconstruction wants
    /// viewpoint diversity, not frame rate, and this keeps the data rate low enough that images can
    /// be sent uncompressed.
    ///
    /// Images and poses are read within a single frame. The two must agree, because the pose the SDK
    /// returns corresponds to the capture time of the latest image; reading them a frame apart
    /// silently offsets every measurement derived from them.
    /// </summary>
    public class KeyframeSource : MonoBehaviour
    {
        const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        [Header("Capture")]
        [SerializeField] Vector2Int requestedResolution = new Vector2Int(1280, 960);

        [Header("Selection")]
        [Tooltip("Metres of translation since the last accepted keyframe.")]
        [SerializeField] float translationThreshold = 0.15f;
        [Tooltip("Degrees of rotation since the last accepted keyframe.")]
        [SerializeField] float rotationThreshold = 15f;
        [Tooltip("Seconds between keyframes, regardless of movement.")]
        [SerializeField] float minInterval = 0.3f;

        [Header("Rejection")]
        [Tooltip("Degrees per second above which motion blur spoils stereo matching.")]
        [SerializeField] float maxAngularSpeed = 60f;
        [Tooltip("Seconds to skip after tracking jumps, letting the pose settle.")]
        [SerializeField] float settleAfterEpoch = 0.5f;

        /// <summary>Raised on the main thread for each accepted keyframe.</summary>
        public event Action<Keyframe> KeyframeReady;

        PassthroughCameraAccess _left, _right;

        int _index;
        int _epoch;
        float _epochChangedAt = float.NegativeInfinity;

        bool _hasPrevious;
        Pose _previousPose;
        bool _hasAccepted;
        Pose _acceptedPose;
        float _acceptedAt = float.NegativeInfinity;

        IEnumerator Start()
        {
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission);
                yield return new WaitForSeconds(1f);
            }

            // Single availability gate; nothing below re-checks.
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
                !PassthroughCameraAccess.IsSupported)
            {
                Debug.LogError("[EnvLine] Passthrough camera unavailable. Disabling.");
                enabled = false;
                yield break;
            }

            _left = Acquire(PassthroughCameraAccess.CameraPositionType.Left);
            _right = Acquire(PassthroughCameraAccess.CameraPositionType.Right);

            float deadline = Time.time + 10f;
            yield return new WaitUntil(() => (_left.IsPlaying && _right.IsPlaying) || Time.time > deadline);
            if (!_left.IsPlaying || !_right.IsPlaying)
            {
                Debug.LogError($"[EnvLine] cameras did not start " +
                               $"(left={_left.IsPlaying}, right={_right.IsPlaying}). Disabling.");
                enabled = false;
                yield break;
            }

            Debug.Log($"[EnvLine] capturing {_left.CurrentResolution.x}x{_left.CurrentResolution.y} stereo");
            StartCoroutine(CaptureLoop());
        }

        PassthroughCameraAccess Acquire(PassthroughCameraAccess.CameraPositionType position)
        {
            // Only one component may own a given camera, so adopt an existing one rather than
            // competing with it.
            foreach (var existing in FindObjectsByType<PassthroughCameraAccess>(FindObjectsSortMode.None))
                if (existing.CameraPosition == position) return existing;

            // The component claims its camera in OnEnable, which AddComponent triggers immediately,
            // so configure it while inactive and activate once the position is set.
            var go = new GameObject($"PassthroughCamera_{position}");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var pca = go.AddComponent<PassthroughCameraAccess>();
            pca.CameraPosition = position;
            pca.RequestedResolution = requestedResolution;

            go.SetActive(true);
            return pca;
        }

        IEnumerator CaptureLoop()
        {
            while (true)
            {
                // End of frame, so the render thread has finished updating the camera textures.
                yield return new WaitForEndOfFrame();
                if (!_left.IsUpdatedThisFrame || !_right.IsUpdatedThisFrame) continue;

                Pose leftPose = _left.GetCameraPose();
                Pose rightPose = _right.GetCameraPose();

                if (!ShouldAccept(leftPose)) continue;

                _acceptedPose = leftPose;
                _acceptedAt = Time.time;
                _hasAccepted = true;

                Vector2Int size = _left.CurrentResolution;
                var keyframe = new Keyframe
                {
                    index = _index++,
                    epoch = _epoch,
                    leftTimestampTicks = _left.Timestamp.Ticks,
                    rightTimestampTicks = _right.Timestamp.Ticks,
                    width = size.x,
                    height = size.y,
                    leftPoseCv = CameraGeometry.FlipY(leftPose),
                    rightPoseCv = CameraGeometry.FlipY(rightPose),
                    leftK = DeriveK(_left),
                    rightK = DeriveK(_right),
                    leftRgb = PackRgb(_left),
                    rightRgb = PackRgb(_right)
                };

                KeyframeReady?.Invoke(keyframe);
            }
        }

        /// <summary>
        /// Tracks pose continuity and decides whether this frame is worth keeping. Rejection and
        /// epoch tracking live here because both depend on the same remembered pose.
        /// </summary>
        bool ShouldAccept(Pose pose)
        {
            float now = Time.time;
            bool accept = true;

            if (_hasPrevious)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                float linearSpeed = Vector3.Distance(pose.position, _previousPose.position) / dt;
                float angularSpeed = Quaternion.Angle(pose.rotation, _previousPose.rotation) / dt;

                // A jump this large is tracking relocating or the user recentering, not motion.
                // Everything reconstructed before it is in a different frame, so mark a new epoch.
                if (linearSpeed > 3f || angularSpeed > 300f)
                {
                    _epoch++;
                    _epochChangedAt = now;
                    _hasAccepted = false;
                    Debug.LogWarning($"[EnvLine] tracking discontinuity, epoch now {_epoch}");
                    accept = false;
                }
                else if (angularSpeed > maxAngularSpeed)
                {
                    // Motion blur corrupts stereo matching directly.
                    accept = false;
                }
            }

            _previousPose = pose;
            _hasPrevious = true;

            if (now - _epochChangedAt < settleAfterEpoch) return false;
            if (!accept) return false;
            if (!_hasAccepted) return true;
            if (now - _acceptedAt < minInterval) return false;

            return Vector3.Distance(pose.position, _acceptedPose.position) >= translationThreshold
                || Quaternion.Angle(pose.rotation, _acceptedPose.rotation) >= rotationThreshold;
        }

        static CameraGeometry.Intrinsics DeriveK(PassthroughCameraAccess pca)
        {
            var p = pca.Intrinsics;
            return CameraGeometry.DeriveIntrinsics(p.FocalLength, p.PrincipalPoint,
                                                   p.SensorResolution, pca.CurrentResolution);
        }

        /// <summary>
        /// Copies the camera image into tightly packed RGB, top row first.
        ///
        /// Rows are reversed because the SDK returns them bottom-up while OpenCV, and the
        /// intrinsics produced by <see cref="CameraGeometry"/>, expect top-down.
        /// </summary>
        static byte[] PackRgb(PassthroughCameraAccess pca)
        {
            var colors = pca.GetColors();
            int width = pca.CurrentResolution.x;
            int height = pca.CurrentResolution.y;

            // GetColors returns a buffer four times the pixel count, so bound the copy by the
            // image size rather than by colors.Length.
            var rgb = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            {
                int source = y * width;
                int destination = (height - 1 - y) * width * 3;
                for (int x = 0; x < width; x++)
                {
                    Color32 c = colors[source + x];
                    rgb[destination++] = c.r;
                    rgb[destination++] = c.g;
                    rgb[destination++] = c.b;
                }
            }
            return rgb;
        }
    }
}
