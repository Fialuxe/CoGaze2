namespace CoGaze.Core
{
    /// <summary>The current condition, readable by anyone, writable only by the TrialRunner.</summary>
    public interface IConditionSource
    {
        bool HasCondition { get; }
        Condition Current { get; }
        bool InTrial { get; }
        /// <summary>Index into the participant's trial order, -1 before the first.</summary>
        int TrialIndex { get; }
    }
}
