using System;
using CoGaze.Clock;
using CoGaze.Core;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class ClockTests
    {
        [Test]
        public void TicksToNs_IsExactForLargeCounts()
        {
            const long freq = 10_000_000; // typical QPC frequency
            long ticks = 365L * 24 * 3600 * freq + 1234567; // a year of uptime plus a remainder
            long expected = 365L * 24 * 3600 * 1_000_000_000L + 123456700;
            Assert.AreEqual(expected, StopwatchClock.TicksToNs(ticks, freq));
        }

        [Test]
        public void StopwatchClock_IsMonotonic()
        {
            var c = new StopwatchClock();
            long a = c.NowNs, b = c.NowNs;
            Assert.GreaterOrEqual(b, a);
        }

        [Test]
        public void OffsetEstimator_UsesMinimumRoundTrip()
        {
            const long trueOffset = 5_000_000; // worker is 5 ms ahead
            var rng = new Random(3);
            var est = new ClockOffsetEstimator(window: 50);
            long t = 1_000_000_000;
            for (uint i = 0; i < 50; i++)
            {
                long up = 1_000_000 + rng.Next(0, 20_000_000);   // asymmetric delays, 1..21 ms
                long down = 1_000_000 + rng.Next(0, 20_000_000);
                long tReceive = t + up + trueOffset;
                long tReply = tReceive + 100_000;
                long tBack = tReply - trueOffset + down;
                est.Add(new ClockPing(i, t, tReceive, tReply), tBack);
                t += 100_000_000;
            }
            var best = est.Best;
            Assert.LessOrEqual(Math.Abs(best.OffsetNs - trueOffset), best.RttNs / 2);
            Assert.Less(best.RttNs, 10_000_000);
        }

        [Test]
        public void OffsetEstimator_SymmetricTripIsExact()
        {
            var est = new ClockOffsetEstimator();
            var trip = est.Add(new ClockPing(1, 0, 2_000 + 700, 2_000 + 800), 4_100);
            Assert.AreEqual(4_000, trip.RttNs);
            Assert.AreEqual(700, trip.OffsetNs);
        }
    }
}
