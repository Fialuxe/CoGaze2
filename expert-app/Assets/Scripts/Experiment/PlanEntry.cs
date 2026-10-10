using CoGaze.Core;

namespace CoGaze.Expert.Experiment
{
    /// <summary>One step of a participant's order: which condition, and whether it counts.</summary>
    public readonly struct PlanEntry
    {
        public readonly string ConditionId;
        public readonly TrialKind Kind;

        public PlanEntry(string conditionId, TrialKind kind)
        {
            ConditionId = conditionId;
            Kind = kind;
        }
    }
}
