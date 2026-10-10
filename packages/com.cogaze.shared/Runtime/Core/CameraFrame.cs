using System.Numerics;

namespace CoGaze.Core
{
    /// <summary>
    /// Pose and intrinsics of the worker camera for one video frame (WRK-F-011). Position and
    /// Rotation are Unity world (left-handed, y up, metres); intrinsics are OpenCV pixels of the
    /// delivered image (top-left origin).
    /// </summary>
    public readonly struct CameraFrame
    {
        public readonly long VideoFrameId;
        public readonly long TCaptureNs;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float Fx, Fy, Cx, Cy;
        public readonly int Width, Height;

        public CameraFrame(long videoFrameId, long tCaptureNs, Vector3 position, Quaternion rotation,
                           float fx, float fy, float cx, float cy, int width, int height)
        {
            VideoFrameId = videoFrameId;
            TCaptureNs = tCaptureNs;
            Position = position;
            Rotation = rotation;
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
            Width = width;
            Height = height;
        }
    }
}
