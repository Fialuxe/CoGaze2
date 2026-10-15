using System;
using System.IO;
using CoGaze.Core;

namespace CoGaze.Logging
{
    /// <summary>Log queue depth, writer errors and free disk space on the checklist.</summary>
    public sealed class LogHealthReporter
    {
        readonly CsvLogSink _sink;
        readonly IHealthBoard _health;
        readonly IClock _clock;
        readonly string _directory;
        readonly int _warnDepth;
        readonly double _minDiskGb;
        long _nextNs;

        public LogHealthReporter(CsvLogSink sink, IHealthBoard health, IClock clock, string directory, int warnDepth, double minDiskGb)
        {
            _sink = sink;
            _health = health;
            _clock = clock;
            _directory = directory;
            _warnDepth = warnDepth;
            _minDiskGb = minDiskGb;
        }

        public void Tick()
        {
            long now = _clock.NowNs;
            if (now < _nextNs) return;
            _nextNs = now + 1_000_000_000L;

            int depth = _sink.QueueDepth;
            if (_sink.Error != null)
                _health.Set(new HealthItem("log_queue", "Log writer", HealthLevel.Error, "error",
                    $"Cannot write logs: {_sink.Error}", "Stop the session and check the disk."));
            else if (depth > _warnDepth)
                _health.Set(new HealthItem("log_queue", "Log writer", HealthLevel.Error, $"{depth} rows queued",
                    "Logs are not keeping up with the data.", "Close other programs using the disk."));
            else
                _health.Set(new HealthItem("log_queue", "Log writer", HealthLevel.Ok, $"{depth} queued"));

            double freeGb = FreeGb();
            _health.Set(freeGb >= _minDiskGb
                ? new HealthItem("disk", "Disk space", HealthLevel.Ok, $"{freeGb:0} GB free")
                : new HealthItem("disk", "Disk space", HealthLevel.Error, $"{freeGb:0.0} GB free",
                    $"Less than {_minDiskGb:0} GB free.", "Free disk space before starting."));
        }

        double FreeGb()
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_directory))).AvailableFreeSpace / 1e9; }
            catch (Exception) { return double.NaN; }
        }
    }
}
