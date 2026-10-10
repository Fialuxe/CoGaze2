using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>sigma_source = fixed: the condition's fixed_sigma.</summary>
    public sealed class FixedSigmaPolicy : ISigmaPolicy
    {
        readonly IConditionSource _conditions;

        public FixedSigmaPolicy(IConditionSource conditions)
        {
            _conditions = conditions;
        }

        public float SigmaDeg(in GazeSample sample, in GazeRay ray) => _conditions.Current.FixedSigmaDeg;
    }
}
