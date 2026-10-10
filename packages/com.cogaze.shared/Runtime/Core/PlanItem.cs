namespace CoGaze.Core
{
    /// <summary>One entry of the participant's trial order, as the experimenter screen lists it.</summary>
    public readonly struct PlanItem
    {
        public readonly int Index;
        public readonly TrialKind Kind;
        public readonly Condition Condition;
        public readonly PlanItemState State;

        public PlanItem(int index, TrialKind kind, Condition condition, PlanItemState state)
        {
            Index = index;
            Kind = kind;
            Condition = condition;
            State = state;
        }
    }
}
