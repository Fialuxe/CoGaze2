namespace CoGaze.Core
{
    /// <summary>
    /// Camera pose per video frame for the last few seconds (WRK-F-012). Capture adds; the cue
    /// pipeline looks up the frame the expert was looking at.
    /// </summary>
    public interface IFramePoseHistory
    {
        void Add(in CameraFrame frame);
        bool TryGet(long videoFrameId, out CameraFrame frame);
        bool TryLatest(out CameraFrame frame);
    }
}
