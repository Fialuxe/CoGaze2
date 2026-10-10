using System.Threading;
using CoGaze.Core;

namespace CoGaze.Worker.Session
{
    /// <summary>
    /// The worker's log until the expert names the session: rows are dropped (and counted) until a
    /// real sink is attached, because the worker has no folder to write them to before then
    /// (SRS 3.4: the worker's folder carries the expert's session name).
    /// </summary>
    public sealed class ForwardingLogSink : ILogSink
    {
        volatile ILogSink _target;
        int _dropped;

        public int Dropped => Volatile.Read(ref _dropped);
        public bool Attached => _target != null;

        public void Attach(ILogSink target) => _target = target;

        public void Write(string table, string row)
        {
            var t = _target;
            if (t != null) t.Write(table, row);
            else Interlocked.Increment(ref _dropped);
        }
    }
}
