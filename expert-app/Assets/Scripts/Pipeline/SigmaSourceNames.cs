using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>The spelling used in conditions.json and the logs.</summary>
    public static class SigmaSourceNames
    {
        public static string Of(SigmaSource s)
        {
            switch (s)
            {
                case SigmaSource.Fixed: return "fixed";
                case SigmaSource.Calib: return "calib";
                default: return "calib_live";
            }
        }
    }
}
