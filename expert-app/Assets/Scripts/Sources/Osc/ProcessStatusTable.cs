using System.Collections.Generic;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>
    /// Last /cogaze/status per process (EXP-F-001). A process that has been silent longer than the
    /// timeout counts as stopped (EXP-F-003). Updated from the UDP thread, read on the main thread.
    /// </summary>
    public sealed class ProcessStatusTable
    {
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly string State;
            public readonly float RateHz;
            public readonly long TReceiveNs;

            public Entry(string name, string state, float rateHz, long tReceiveNs)
            {
                Name = name;
                State = state;
                RateHz = rateHz;
                TReceiveNs = tReceiveNs;
            }

            public bool IsAlive(long nowNs, long timeoutNs) => nowNs - TReceiveNs <= timeoutNs;
        }

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        readonly object _lock = new object();

        public void Update(string name, long tSenderNs, string state, float rateHz, long tReceiveNs)
        {
            lock (_lock) _entries[name] = new Entry(name, state, rateHz, tReceiveNs);
        }

        public bool TryGet(string name, out Entry entry)
        {
            lock (_lock) return _entries.TryGetValue(name, out entry);
        }
    }
}
