namespace CoGaze.Core
{
    /// <summary>The aligned work surface (WRK-F-002..005).</summary>
    public interface IWorkSurface
    {
        AlignmentState State { get; }
        bool TryGetPlane(out WorkPlane plane);
    }
}
