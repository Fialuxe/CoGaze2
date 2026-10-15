using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>
    /// One checklist row per gaze source: its sample rate over the last second, red when nothing has
    /// arrived for the timeout. Reads the pipeline's counters, so it sees exactly what the pipeline saw.
    /// </summary>
    public sealed class SourceHealthReporter
    {
        readonly GazePipeline _pipeline;
        readonly IHealthBoard _health;
        readonly IClock _clock;
        readonly long _timeoutNs;
        readonly Dictionary<string, string> _actions;
        readonly Dictionary<string, (int count, long tNs, long lastChangeNs)> _last = new Dictionary<string, (int, long, long)>();
        long _nextNs;

        /// <param name="sources">source name → what the experimenter should do when it stops.</param>
        public SourceHealthReporter(GazePipeline pipeline, IHealthBoard health, IClock clock, float timeoutS,
                                    Dictionary<string, string> sources)
        {
            _pipeline = pipeline;
            _health = health;
            _clock = clock;
            _timeoutNs = (long)(timeoutS * 1e9);
            _actions = sources;
            foreach (var kv in sources)
                _health.Set(new HealthItem(Key(kv.Key), Label(kv.Key), HealthLevel.Unknown, "waiting"));
        }

        static string Key(string source) => "source_" + source;
        static string Label(string source) => "Gaze · " + source;

        public void Tick()
        {
            long now = _clock.NowNs;
            if (now < _nextNs) return;
            _nextNs = now + 1_000_000_000L;
            foreach (var kv in _actions)
            {
                _pipeline.Received.TryGetValue(kv.Key, out int count);
                if (!_last.TryGetValue(kv.Key, out var prev)) prev = (0, now, now);
                long lastChange = count != prev.count ? now : prev.lastChangeNs;
                double hz = (count - prev.count) / ((now - prev.tNs) / 1e9 + 1e-9);
                _last[kv.Key] = (count, now, lastChange);

                if (now - lastChange > _timeoutNs)
                    _health.Set(new HealthItem(Key(kv.Key), Label(kv.Key), HealthLevel.Error, "0 Hz",
                        $"No samples from {kv.Key} for {(now - lastChange) / 1e9:0} s.", kv.Value));
                else if (count > 0)
                    _health.Set(new HealthItem(Key(kv.Key), Label(kv.Key), HealthLevel.Ok, $"{hz:0} Hz"));
            }
        }
    }
}
