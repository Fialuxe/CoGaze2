namespace CoGaze.Core
{
    /// <summary>Sends gaze to the worker. Only the expert GazePipeline holds one (EXP-F-030).</summary>
    public interface ICueSender
    {
        void Send(in GazeCue cue);
    }
}
