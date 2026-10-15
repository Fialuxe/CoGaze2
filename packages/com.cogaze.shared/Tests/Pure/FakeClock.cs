using CoGaze.Core;

namespace CoGaze.Shared.Tests
{
    /// <summary>A clock tests move by hand.</summary>
    public sealed class FakeClock : IClock
    {
        public long NowNs { get; set; }
        public void AdvanceMs(double ms) => NowNs += (long)(ms * 1_000_000);
    }
}
