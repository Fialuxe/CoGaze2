using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Logging
{
    /// <summary>
    /// The checklist's model. Every level change is written to events.csv, so the log shows when
    /// something went red even if nobody was looking at the screen.
    /// </summary>
    public sealed class HealthBoard : IHealthBoard
    {
        readonly ILogSink _log;
        readonly IClock _clock;
        readonly List<HealthItem> _items = new List<HealthItem>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();

        public HealthBoard(ILogSink log, IClock clock)
        {
            _log = log;
            _clock = clock;
        }

        public IReadOnlyList<HealthItem> Items => _items;

        public bool AllOk
        {
            get
            {
                if (_items.Count == 0) return false;
                foreach (var i in _items)
                    if (i.Level != HealthLevel.Ok) return false;
                return true;
            }
        }

        public void Set(in HealthItem item)
        {
            if (_index.TryGetValue(item.Key, out int at))
            {
                var before = _items[at];
                _items[at] = item;
                if (before.Level == item.Level) return;
            }
            else
            {
                _index[item.Key] = _items.Count;
                _items.Add(item);
            }
            _log?.Event(_clock.NowNs, EventKinds.Health, $"{item.Key}={item.Level.ToString().ToLowerInvariant()} {item.Problem}".Trim());
        }

        public bool TryGet(string key, out HealthItem item)
        {
            if (_index.TryGetValue(key, out int at)) { item = _items[at]; return true; }
            item = default;
            return false;
        }
    }
}
