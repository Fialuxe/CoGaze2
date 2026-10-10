using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Worker.Session
{
    /// <summary>
    /// pose.csv at a fixed rate (WRK-F-034, 30 Hz provisional): head and both hands. An untracked
    /// hand leaves its fields empty rather than repeating a stale pose. Call Tick every frame; rows
    /// are written on the rate's schedule, which holds on average even when frames are uneven.
    /// </summary>
    public sealed class PoseLogger
    {
        readonly ILogSink _log;
        readonly long _periodNs;
        readonly List<object> _fields = new List<object>(22);
        long _nextNs = long.MinValue;

        public PoseLogger(ILogSink log, float rateHz = 30f)
        {
            _log = log;
            _periodNs = (long)(1e9 / rateHz);
        }

        public void Tick(long nowNs, in TrackedPose head, in TrackedPose leftHand, in TrackedPose rightHand)
        {
            if (_nextNs == long.MinValue) _nextNs = nowNs;
            if (nowNs < _nextNs) return;
            _nextNs += _periodNs;
            if (_nextNs <= nowNs) _nextNs = nowNs + _periodNs;   // after a stall, do not burst to catch up

            _fields.Clear();
            _fields.Add(nowNs);
            Add(head);
            Add(leftHand);
            Add(rightHand);
            _log.Write(LogSchema.Pose, Csv.Row(_fields.ToArray()));
        }

        void Add(in TrackedPose p)
        {
            if (!p.Tracked)
            {
                for (int i = 0; i < 7; i++) _fields.Add(null);
                return;
            }
            _fields.Add(p.Position.X);
            _fields.Add(p.Position.Y);
            _fields.Add(p.Position.Z);
            _fields.Add(p.Rotation.X);
            _fields.Add(p.Rotation.Y);
            _fields.Add(p.Rotation.Z);
            _fields.Add(p.Rotation.W);
        }
    }
}
