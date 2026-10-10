using System.Diagnostics;
using CoGaze.Core;

namespace CoGaze.Clock
{
    /// <summary>
    /// QPC (Stopwatch) ticks as int64 nanoseconds (COM-F-001). Same timebase as Python's
    /// time.perf_counter_ns() on Windows, so expert-PC processes share one clock.
    /// </summary>
    public sealed class StopwatchClock : IClock
    {
        public long NowNs => TicksToNs(Stopwatch.GetTimestamp(), Stopwatch.Frequency);

        /// <summary>Split into whole seconds and remainder so large tick counts do not overflow.</summary>
        public static long TicksToNs(long ticks, long frequency)
        {
            long seconds = ticks / frequency;
            long remainder = ticks % frequency;
            return seconds * 1_000_000_000L + remainder * 1_000_000_000L / frequency;
        }
    }
}
