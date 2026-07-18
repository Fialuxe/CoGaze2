using UnityEngine;

namespace CoGaze2.EnvLine
{
    /// <summary>
    /// Camera geometry for the environment line: intrinsics derivation and Unity/OpenCV
    /// convention conversion. Pure functions only — no state, no Unity lifecycle, no Meta SDK
    /// dependency, so this compiles and unit-tests without a headset or the XR packages.
    ///
    /// The correctness of the whole reconstruction pipeline rests on this file. Everything here
    /// is a hypothesis until GeometryProbe's Test A matches it against the MRUK oracle on device.
    /// </summary>
    public static class CameraGeometry
    {
        /// <summary>Intrinsics for the delivered image, OpenCV convention (origin top-left, +v down).</summary>
        public struct Intrinsics
        {
            public float fx, fy, cx, cy;
            public int width, height;
        }

        /// <summary>
        /// Replicates MRUK PassthroughCameraAccess.CalcSensorCropRegion. The delivered image is a
        /// centered crop of the sensor, so sensor-space intrinsics cannot be used directly.
        /// </summary>
        public static Rect SensorCropRegion(Vector2Int sensorRes, Vector2Int currentRes)
        {
            Vector2 scale = new Vector2((float)currentRes.x / sensorRes.x,
                                        (float)currentRes.y / sensorRes.y);
            scale /= Mathf.Max(scale.x, scale.y);
            return new Rect(sensorRes.x * (1f - scale.x) * 0.5f,
                            sensorRes.y * (1f - scale.y) * 0.5f,
                            sensorRes.x * scale.x,
                            sensorRes.y * scale.y);
        }

        /// <summary>
        /// Converts MRUK sensor-space intrinsics into OpenCV intrinsics for the delivered image.
        /// MRUK's viewport y is up-positive; OpenCV's v is down-positive, hence the cy flip.
        /// </summary>
        /// <param name="halfPixelOffset">
        /// Whether to apply the -0.5 continuous-to-pixel-center correction. Undetermined until
        /// Test A runs on device — try both and keep whichever lands under 0.5 px.
        /// </param>
        public static Intrinsics DeriveIntrinsics(Vector2 focalLength, Vector2 principalPoint,
                                                  Vector2Int sensorRes, Vector2Int currentRes,
                                                  bool halfPixelOffset)
        {
            Rect crop = SensorCropRegion(sensorRes, currentRes);

            float sx = currentRes.x / crop.width;
            float sy = currentRes.y / crop.height;

            var k = new Intrinsics
            {
                fx = focalLength.x * sx,
                fy = focalLength.y * sy,
                cx = (principalPoint.x - crop.x) * sx,
                // viewport-space (bottom-left origin) principal point, flipped to OpenCV top-left
                cy = currentRes.y - (principalPoint.y - crop.y) * sy,
                width = currentRes.x,
                height = currentRes.y
            };

            if (halfPixelOffset) { k.cx -= 0.5f; k.cy -= 0.5f; }
            return k;
        }

        /// <summary>
        /// Converts a pose between Unity (left-handed, Y-up) and OpenCV (right-handed, Y-down).
        /// Both put +Z forward, so a single Y flip — S = diag(1,-1,1) — reconciles them.
        ///
        /// S is involutive (S*S = I), so this same call converts in both directions: use it on the
        /// outbound camera pose, and again on mesh vertices coming back from the server.
        /// Conjugating a rotation by S maps quaternion (x,y,z,w) to (-x,y,-z,w); det stays +1, so
        /// the result is a rotation and not a mirror.
        /// </summary>
        public static Pose FlipY(Pose pose)
        {
            var p = pose.position;
            var q = pose.rotation;
            return new Pose(new Vector3(p.x, -p.y, p.z),
                            new Quaternion(-q.x, q.y, -q.z, q.w));
        }

        /// <summary>Y-flips a bare point (mesh vertices coming back from the server).</summary>
        public static Vector3 FlipY(Vector3 v) => new Vector3(v.x, -v.y, v.z);

        /// <summary>
        /// Projects an OpenCV-world point through an OpenCV world_T_cam pose and intrinsics.
        /// Returns false when the point is at or behind the camera plane.
        /// </summary>
        public static bool Project(Vector3 worldCv, Pose worldTCamCv, in Intrinsics k,
                                   out Vector2 pixel, out float depth)
        {
            Vector3 pCam = Quaternion.Inverse(worldTCamCv.rotation) * (worldCv - worldTCamCv.position);
            depth = pCam.z;
            if (depth <= 0f) { pixel = default; return false; }

            pixel = new Vector2(k.fx * pCam.x / depth + k.cx,
                                k.fy * pCam.y / depth + k.cy);
            return true;
        }

        /// <summary>
        /// OpenCV pixel (top-left origin) to MRUK viewport (bottom-left origin, normalised).
        /// Used to compare our projection against PassthroughCameraAccess.WorldToViewportPoint.
        /// </summary>
        public static Vector2 PixelToViewport(Vector2 pixel, in Intrinsics k)
            => new Vector2(pixel.x / k.width, 1f - pixel.y / k.height);
    }
}
