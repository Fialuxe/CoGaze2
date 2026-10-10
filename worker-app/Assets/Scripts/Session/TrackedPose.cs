using System.Numerics;

namespace CoGaze.Worker.Session
{
    /// <summary>A head or hand pose in Unity world (left-handed, y up, metres), or not tracked.</summary>
    public readonly struct TrackedPose
    {
        public readonly bool Tracked;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public TrackedPose(bool tracked, Vector3 position, Quaternion rotation)
        {
            Tracked = tracked;
            Position = position;
            Rotation = rotation;
        }

        public static TrackedPose None => default;
    }
}
