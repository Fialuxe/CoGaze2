using System;
using System.Numerics;
using CoGaze.Core;

namespace CoGaze.Worker.Cue
{
    /// <summary>Pure geometry for placing a cue: video uv → world ray → work-plane hit (WRK-F-020).</summary>
    public static class CueMath
    {
        /// <summary>
        /// Ray through normalised image point (u, v) of a camera frame, in Unity world. Intrinsics are
        /// OpenCV (v down), the pose is Unity (y up), so the camera-space y is negated — the same
        /// convention as CameraGeometry.FlipY.
        /// </summary>
        public static void Unproject(in CameraFrame f, float u, float v, out Vector3 origin, out Vector3 direction)
        {
            float x = (u * f.Width - f.Cx) / f.Fx;
            float y = (v * f.Height - f.Cy) / f.Fy;
            var local = Vector3.Normalize(new Vector3(x, -y, 1f));
            origin = f.Position;
            direction = Vector3.Transform(local, f.Rotation);
        }

        /// <summary>Intersection with the plane in front of the origin; false when parallel or behind.</summary>
        public static bool Intersect(Vector3 origin, Vector3 direction, in WorkPlane plane, out Vector3 hit, out float distance)
        {
            float denom = Vector3.Dot(direction, plane.Normal);
            hit = default;
            distance = 0f;
            if (Math.Abs(denom) < 1e-6f) return false;
            distance = Vector3.Dot(plane.Origin - origin, plane.Normal) / denom;
            if (distance <= 0f) return false;
            hit = origin + direction * distance;
            return true;
        }

        /// <summary>WRK-F-023: radius on the plane = distance × tan(sigma).</summary>
        public static float RadiusForSigma(float distanceM, float sigmaDeg) =>
            distanceM * (float)Math.Tan(sigmaDeg * Math.PI / 180.0);
    }
}
