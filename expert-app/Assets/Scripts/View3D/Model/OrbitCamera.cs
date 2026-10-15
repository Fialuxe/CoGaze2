using System;
using System.Numerics;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// The expert's viewpoint control: orbit, pan, zoom, frame-all and look-from. Input moves the
    /// <see cref="Goal"/> at once; <see cref="Current"/> eases after it each frame, so motion is smooth
    /// but never lags more than a few frames.
    /// </summary>
    public sealed class OrbitCamera
    {
        public const float MinDistanceM = 0.05f;
        public const float MaxDistanceM = 30f;
        public const float MaxPitchDeg = 89f;

        public float DegPerPx = 0.25f;
        /// <summary>Distance multiplier per wheel notch towards the target.</summary>
        public float ZoomPerStep = 0.85f;
        /// <summary>Easing rate: about 95% of the way in 3 / SharpnessPerS seconds.</summary>
        public float SharpnessPerS = 20f;

        public OrbitPose Goal { get; private set; }
        public OrbitPose Current { get; private set; }
        /// <summary>True once the expert has steered; automatic framing stops then.</summary>
        public bool UserMoved { get; private set; }

        public OrbitCamera(OrbitPose start)
        {
            Goal = start;
            Current = start;
        }

        /// <summary>Drag in screen px (y up): right turns the view right, up tilts it up.</summary>
        public void Orbit(float dxPx, float dyPx)
        {
            var g = Goal;
            float pitch = Math.Clamp(g.PitchDeg - dyPx * DegPerPx, -MaxPitchDeg, MaxPitchDeg);
            Goal = new OrbitPose(g.Target, g.YawDeg + dxPx * DegPerPx, pitch, g.DistanceM);
            UserMoved = true;
        }

        /// <summary>Grab-and-drag: the point under the cursor follows it.</summary>
        public void Pan(float dxPx, float dyPx, float viewportHeightPx, float verticalFovDeg)
        {
            var g = Goal;
            float metresPerPx = 2f * g.DistanceM * MathF.Tan(verticalFovDeg * (float)(Math.PI / 360.0)) / Math.Max(viewportHeightPx, 1f);
            var target = g.Target - (g.Right * dxPx + g.Up * dyPx) * metresPerPx;
            Goal = new OrbitPose(target, g.YawDeg, g.PitchDeg, g.DistanceM);
            UserMoved = true;
        }

        /// <summary>Positive steps (wheel forward) move closer.</summary>
        public void Zoom(float steps)
        {
            var g = Goal;
            float d = Math.Clamp(g.DistanceM * MathF.Pow(ZoomPerStep, steps), MinDistanceM, MaxDistanceM);
            Goal = new OrbitPose(g.Target, g.YawDeg, g.PitchDeg, d);
            UserMoved = true;
        }

        /// <summary>Centres the box and backs off until its bounding sphere fits; keeps the heading.</summary>
        public void Frame(Vector3 min, Vector3 max, float verticalFovDeg, float aspect)
        {
            var g = Goal;
            float radius = Math.Max(Vector3.Distance(min, max) / 2f, 0.05f);
            float halfV = verticalFovDeg * (float)(Math.PI / 360.0);
            float half = Math.Min(halfV, MathF.Atan(MathF.Tan(halfV) * aspect));
            float d = Math.Clamp(radius / MathF.Sin(half) * 1.05f, MinDistanceM, MaxDistanceM);
            Goal = new OrbitPose((min + max) / 2f, g.YawDeg, g.PitchDeg, d);
        }

        /// <summary>Puts the camera at a point facing a direction (e.g. the worker's head), orbiting a point ahead.</summary>
        public void LookFrom(Vector3 position, Vector3 forward, float distanceM)
        {
            var f = Vector3.Normalize(forward);
            float yaw = MathF.Atan2(f.X, f.Z) * (float)(180.0 / Math.PI);
            float pitch = Math.Clamp(MathF.Asin(Math.Clamp(-f.Y, -1f, 1f)) * (float)(180.0 / Math.PI), -MaxPitchDeg, MaxPitchDeg);
            // Keep yaw continuous with where we are, so easing turns the short way round.
            yaw += 360f * MathF.Round((Current.YawDeg - yaw) / 360f);
            var pose = new OrbitPose(Vector3.Zero, yaw, pitch, distanceM);
            Goal = new OrbitPose(position + pose.Forward * distanceM, yaw, pitch, distanceM);
        }

        /// <summary>Moves <see cref="Current"/> towards <see cref="Goal"/>; frame-rate independent.</summary>
        public void Tick(float dtS)
        {
            float t = 1f - MathF.Exp(-SharpnessPerS * Math.Max(dtS, 0f));
            Current = OrbitPose.Lerp(Current, Goal, t);
        }
    }
}
