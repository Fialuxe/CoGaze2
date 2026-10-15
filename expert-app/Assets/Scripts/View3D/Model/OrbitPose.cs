using System;
using System.Numerics;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// Where the expert's virtual camera is: a point it orbits, a heading, a tilt and a distance.
    /// Unity world (left-handed, +Y up, metres); yaw turns about +Y, positive pitch looks down — the
    /// same as Quaternion.Euler(PitchDeg, YawDeg, 0).
    /// </summary>
    public readonly struct OrbitPose
    {
        public readonly Vector3 Target;
        public readonly float YawDeg;
        public readonly float PitchDeg;
        public readonly float DistanceM;

        public OrbitPose(Vector3 target, float yawDeg, float pitchDeg, float distanceM)
        {
            Target = target;
            YawDeg = yawDeg;
            PitchDeg = pitchDeg;
            DistanceM = distanceM;
        }

        static float Rad(float deg) => deg * (float)(Math.PI / 180.0);

        public Vector3 Forward
        {
            get
            {
                float y = Rad(YawDeg), p = Rad(PitchDeg);
                return new Vector3(MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p), MathF.Cos(p) * MathF.Cos(y));
            }
        }

        public Vector3 Right => new Vector3(MathF.Cos(Rad(YawDeg)), 0f, -MathF.Sin(Rad(YawDeg)));

        public Vector3 Up
        {
            get
            {
                float y = Rad(YawDeg), p = Rad(PitchDeg);
                return new Vector3(MathF.Sin(p) * MathF.Sin(y), MathF.Cos(p), MathF.Sin(p) * MathF.Cos(y));
            }
        }

        public Vector3 Position => Target - Forward * DistanceM;

        /// <summary>Eases a towards b; distance in log space so zooming feels even at any scale.</summary>
        public static OrbitPose Lerp(in OrbitPose a, in OrbitPose b, float t) => new OrbitPose(
            Vector3.Lerp(a.Target, b.Target, t),
            a.YawDeg + (b.YawDeg - a.YawDeg) * t,
            a.PitchDeg + (b.PitchDeg - a.PitchDeg) * t,
            MathF.Exp(MathF.Log(a.DistanceM) + (MathF.Log(b.DistanceM) - MathF.Log(a.DistanceM)) * t));
    }
}
