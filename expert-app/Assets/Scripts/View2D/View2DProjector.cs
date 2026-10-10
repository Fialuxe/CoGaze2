using CoGaze.Core;

namespace CoGaze.Expert.View2D
{
    /// <summary>
    /// 2D path: the gaze becomes a position on the worker's video frame that was on screen at that
    /// moment. The worker turns it into a ray with that frame's camera pose (WRK-F-020).
    /// </summary>
    public sealed class View2DProjector : IGazeRayProjector
    {
        readonly DisplayedFrameLog _frames;
        VideoRect _rect;

        public View2DProjector(VideoRect rect, DisplayedFrameLog frames)
        {
            _rect = rect;
            _frames = frames;
        }

        /// <summary>
        /// Where the picture itself is drawn: the stage rect fitted to the video's aspect, set when the
        /// video's size becomes known. uv is relative to the picture, not the stage (EXP-F-022).
        /// </summary>
        public void SetRect(VideoRect rect) => _rect = rect;

        public bool TryProject(in GazeSample sample, out GazeRay ray)
        {
            bool inside = _rect.TryToUv(sample.XPx, sample.YPx, out float u, out float v);
            ray = new GazeRay(sample.Valid && inside, u, v, _frames.FrameAt(sample.TCaptureNs));
            return ray.Valid;
        }
    }
}
