using CoGaze.Core;

namespace CoGaze.Worker.Cue
{
    /// <summary>
    /// Ring buffer of camera frames, looked up by video_frame_id (WRK-F-012). Sized for the retention
    /// time at the capture rate; lookups older than that fail and the cue falls back to the latest pose.
    /// </summary>
    public sealed class FramePoseHistory : IFramePoseHistory
    {
        readonly CameraFrame[] _frames;
        readonly long _retentionNs;
        int _head, _count;

        public FramePoseHistory(int capacity, float retentionS = 5f)
        {
            _frames = new CameraFrame[capacity];
            _retentionNs = (long)(retentionS * 1e9);
        }

        public void Add(in CameraFrame frame)
        {
            _frames[_head] = frame;
            _head = (_head + 1) % _frames.Length;
            if (_count < _frames.Length) _count++;
        }

        public bool TryLatest(out CameraFrame frame)
        {
            frame = _count > 0 ? _frames[(_head - 1 + _frames.Length) % _frames.Length] : default;
            return _count > 0;
        }

        public bool TryGet(long videoFrameId, out CameraFrame frame)
        {
            if (TryLatest(out var latest))
                for (int k = 1; k <= _count; k++)
                {
                    var f = _frames[(_head - k + _frames.Length) % _frames.Length];
                    if (latest.TCaptureNs - f.TCaptureNs > _retentionNs) break;
                    if (f.VideoFrameId == videoFrameId)
                    {
                        frame = f;
                        return true;
                    }
                }
            frame = default;
            return false;
        }
    }
}
