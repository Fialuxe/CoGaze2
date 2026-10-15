namespace CoGaze.Core
{
    /// <summary>Experimenter actions (EXP-F-004). Bound to keys, so nothing shows on the participant's screen.</summary>
    public interface IExperimentControl
    {
        /// <summary>Send the next condition to the worker (Idle/InterTrial only).</summary>
        void PrepareNext();
        void StartTrial();
        void EndTrial();
        /// <summary>Mark the running trial aborted; the same entry is run again (EXP-F-058).</summary>
        void AbortTrial();
    }
}
