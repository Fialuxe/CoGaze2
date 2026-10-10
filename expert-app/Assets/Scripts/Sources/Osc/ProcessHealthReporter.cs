using CoGaze.Core;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>
    /// Puts each expected Python process on the checklist from its /cogaze/status (EXP-F-001, 003):
    /// red when silent past the timeout or reporting "error".
    /// </summary>
    public sealed class ProcessHealthReporter
    {
        readonly ProcessStatusTable _table;
        readonly IHealthBoard _health;
        readonly IClock _clock;
        readonly long _timeoutNs;
        readonly (string name, string label, string action)[] _expected;

        public ProcessHealthReporter(ProcessStatusTable table, IHealthBoard health, IClock clock, float timeoutS,
                                     params (string name, string label, string action)[] expected)
        {
            _table = table;
            _health = health;
            _clock = clock;
            _timeoutNs = (long)(timeoutS * 1e9);
            _expected = expected;
        }

        public void Tick()
        {
            long now = _clock.NowNs;
            foreach (var (name, label, action) in _expected)
            {
                string key = "process_" + name;
                if (!_table.TryGet(name, out var e))
                    _health.Set(new HealthItem(key, label, HealthLevel.Error, "—", $"No status from {name} yet.", action));
                else if (!e.IsAlive(now, _timeoutNs))
                    _health.Set(new HealthItem(key, label, HealthLevel.Error, "silent",
                        $"{name} has been silent for {(now - e.TReceiveNs) / 1e9:0.0} s.", action));
                else if (e.State == "error")
                    _health.Set(new HealthItem(key, label, HealthLevel.Error, e.State, $"{name} reports an error.", action));
                else
                    _health.Set(new HealthItem(key, label, HealthLevel.Ok, $"{e.RateHz:0.0} Hz · {e.State}"));
            }
        }
    }
}
