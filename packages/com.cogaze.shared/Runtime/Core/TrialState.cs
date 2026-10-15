namespace CoGaze.Core
{
    /// <summary>TrialRunner states. The condition changes only on InterTrial -> Ready (EXP-F-034).</summary>
    public enum TrialState
    {
        Idle,
        Ready,
        InTrial,
        InterTrial,
        Finished,
    }
}
