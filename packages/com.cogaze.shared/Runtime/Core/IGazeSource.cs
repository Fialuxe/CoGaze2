namespace CoGaze.Core
{
    /// <summary>Entry point for gaze. Beam, Tobii, webcam estimators and fakes all look like this.</summary>
    public interface IGazeSource
    {
        /// <summary>"beam", "tobii", "webcam:mobilegaze", ... compared against Condition.DisplaySource.</summary>
        string Name { get; }

        /// <summary>Next unread sample; false when there is none. Call until false each frame.</summary>
        bool TryRead(out GazeSample sample);
    }
}
