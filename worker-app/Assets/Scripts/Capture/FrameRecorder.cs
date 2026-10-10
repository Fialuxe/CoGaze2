using CoGaze.Core;

namespace CoGaze.Worker.Capture
{
    /// <summary>
    /// Bookkeeping for each video frame sent (WRK-F-011, 012): hands out video_frame_ids from 1
    /// (0 means "no frame" on the wire, TBD-04), keeps the frame's pose and intrinsics in the
    /// history the cue path looks up, and writes its frames.csv row.
    /// </summary>
    public sealed class FrameRecorder
    {
        readonly IFramePoseHistory _history;
        readonly ILogSink _log;
        long _lastId;

        public FrameRecorder(IFramePoseHistory history, ILogSink log)
        {
            _history = history;
            _log = log;
        }

        public long LastId => _lastId;

        public long NextId() => ++_lastId;

        public void Record(in CameraFrame f)
        {
            _history.Add(f);
            var p = f.Position;
            var q = f.Rotation;
            _log.Write(LogSchema.Frames, Csv.Row(f.VideoFrameId, f.TCaptureNs, p.X, p.Y, p.Z, q.X, q.Y, q.Z, q.W,
                                                 f.Fx, f.Fy, f.Cx, f.Cy, f.Width, f.Height));
        }
    }
}
