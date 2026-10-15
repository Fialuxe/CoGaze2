namespace CoGaze.Core
{
    /// <summary>Trial start/end sent to the worker (EXP-F-056). Reliable delivery.</summary>
    public readonly struct TrialMessage
    {
        public readonly int TrialIndex;
        public readonly TrialPhase Phase;
        public readonly TrialKind Kind;
        public readonly string ConditionId;
        public readonly string Task;
        /// <summary>Target/distractor layout on the work plane, JSON; empty when the task has none.</summary>
        public readonly string LayoutJson;

        public TrialMessage(int trialIndex, TrialPhase phase, TrialKind kind, string conditionId, string task, string layoutJson)
        {
            TrialIndex = trialIndex;
            Phase = phase;
            Kind = kind;
            ConditionId = conditionId;
            Task = task;
            LayoutJson = layoutJson;
        }
    }
}
