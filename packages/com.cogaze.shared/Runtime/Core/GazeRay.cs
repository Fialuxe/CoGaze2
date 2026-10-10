using System.Numerics;

namespace CoGaze.Core
{
    /// <summary>
    /// A screen gaze turned into something the worker can place. The 2D path fills U, V and
    /// VideoFrameId (COM-F-007); the 3D path additionally fills Origin and Direction in the worker's
    /// world frame.
    /// </summary>
    public readonly struct GazeRay
    {
        public readonly bool Valid;
        public readonly float U;
        public readonly float V;
        public readonly long VideoFrameId;
        public readonly bool Has3D;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        public GazeRay(bool valid, float u, float v, long videoFrameId)
        {
            Valid = valid;
            U = u;
            V = v;
            VideoFrameId = videoFrameId;
            Has3D = false;
            Origin = default;
            Direction = default;
        }

        public GazeRay(bool valid, float u, float v, long videoFrameId, Vector3 origin, Vector3 direction)
        {
            Valid = valid;
            U = u;
            V = v;
            VideoFrameId = videoFrameId;
            Has3D = true;
            Origin = origin;
            Direction = direction;
        }

        public static GazeRay Invalid => new GazeRay(false, float.NaN, float.NaN, -1);
    }
}
