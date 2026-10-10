using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>Chooses fixed / calib / calib_live by the current condition — the one place sigma is decided (EXP-F-047).</summary>
    public sealed class ConditionSigmaPolicy : ISigmaPolicy
    {
        readonly IConditionSource _conditions;
        readonly ISigmaPolicy _fixed, _calib, _calibLive;

        public ConditionSigmaPolicy(IConditionSource conditions, ISigmaPolicy fixedPolicy, ISigmaPolicy calib, ISigmaPolicy calibLive)
        {
            _conditions = conditions;
            _fixed = fixedPolicy;
            _calib = calib;
            _calibLive = calibLive;
        }

        public float SigmaDeg(in GazeSample sample, in GazeRay ray)
        {
            switch (_conditions.Current.SigmaSource)
            {
                case SigmaSource.Fixed: return _fixed.SigmaDeg(sample, ray);
                case SigmaSource.Calib: return _calib.SigmaDeg(sample, ray);
                default: return _calibLive.SigmaDeg(sample, ray);
            }
        }
    }
}
