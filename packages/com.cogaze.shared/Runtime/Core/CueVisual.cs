using System.Numerics;

namespace CoGaze.Core
{
    /// <summary>What a cue renderer draws this frame, in Unity world coordinates.</summary>
    public readonly struct CueVisual
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Hit;
        public readonly Vector3 Normal;
        /// <summary>Circle radius on the plane.</summary>
        public readonly float RadiusM;
        /// <summary>Frustum half-angle.</summary>
        public readonly float HalfAngleDeg;
        /// <summary>0..1, for the fade-out on loss (WRK-F-026).</summary>
        public readonly float Opacity;

        public CueVisual(Vector3 origin, Vector3 hit, Vector3 normal, float radiusM, float halfAngleDeg, float opacity)
        {
            Origin = origin;
            Hit = hit;
            Normal = normal;
            RadiusM = radiusM;
            HalfAngleDeg = halfAngleDeg;
            Opacity = opacity;
        }
    }
}
