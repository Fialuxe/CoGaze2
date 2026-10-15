using System.Numerics;

namespace CoGaze.Core
{
    /// <summary>The desk top in Unity world. Right and Forward span the plane; layout coordinates use them.</summary>
    public readonly struct WorkPlane
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Normal;
        public readonly Vector3 Right;
        public readonly Vector3 Forward;

        public WorkPlane(Vector3 origin, Vector3 normal, Vector3 right, Vector3 forward)
        {
            Origin = origin;
            Normal = Vector3.Normalize(normal);
            Right = Vector3.Normalize(right);
            Forward = Vector3.Normalize(forward);
        }
    }
}
