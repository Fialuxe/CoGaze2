using UnityEngine;

namespace CoGaze.Geometry
{
    /// <summary>
    /// The one place System.Numerics (pure assemblies) meets UnityEngine types (COM-F-008). Both are
    /// Unity world: left-handed, y up, metres — only the type differs.
    /// </summary>
    public static class NumericsConvert
    {
        public static Vector3 ToUnity(this System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
        public static Quaternion ToUnity(this System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static System.Numerics.Quaternion ToNumerics(this Quaternion q) => new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
    }
}
