namespace CoGaze.Core
{
    /// <summary>Monotonic time in integer nanoseconds (QPC on the expert PC). The only clock logs use.</summary>
    public interface IClock
    {
        long NowNs { get; }
    }
}
