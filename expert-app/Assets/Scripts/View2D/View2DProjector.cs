using CoGaze.Core;

namespace CoGaze.Expert.View2D
{
    /// <summary>
    /// 2D path: the gaze becomes a position on the worker's video frame that was on screen at that
    /// moment. The worker turns it into a ray with that frame's camera pose (WRK-F-020).
    /// </summary>
    public sealed class View2DProjector : IGazeRayProjector
    {
        readonly VideoRect _rect;
        readonly DisplayedFrameLog _frames;

        public View2DProjector(VideoRect rect, DisplayedFrameLog frames)
        {
            _rect = rect;
            _frames = frames;
        }

        public bool TryProject(in GazeSample sample, out GazeRay ray)
        {
            bool inside = _rect.TryToUv(sample.XPx, sample.YPx, out float u, out float v);
            ray = new GazeRay(sample.Valid && inside, u, v, _frames.FrameAt(sample.TCaptureNs));
            return ray.Valid;
        }
    }
}
