using CoGaze.Core;

namespace CoGaze.Worker.Cue
{
    /// <summary>The condition the expert last sent, applied as-is. The worker never decides a condition.</summary>
    public sealed class ReceivedCondition : IConditionSource
    {
        public bool HasCondition { get; private set; }
        public Condition Current { get; private set; }
        public bool InTrial { get; private set; }
        public int TrialIndex { get; private set; } = -1;

        public void Apply(in Condition c)
        {
            Current = c;
            HasCondition = true;
        }

        public void Apply(in TrialMessage t)
        {
            TrialIndex = t.TrialIndex;
            InTrial = t.Phase == TrialPhase.Start;
        }
    }
}
