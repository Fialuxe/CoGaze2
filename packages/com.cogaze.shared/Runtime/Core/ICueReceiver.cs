namespace CoGaze.Core
{
    /// <summary>Gaze arriving at the worker, live or replayed.</summary>
    public interface ICueReceiver
    {
        bool TryReceive(out GazeCue cue);
    }
}
