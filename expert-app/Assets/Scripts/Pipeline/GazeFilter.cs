using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>Two One-Euro filters over a sample's screen position. Invalid samples pass untouched.</summary>
    public sealed class GazeFilter
    {
        readonly OneEuroFilter _x, _y;

        public GazeFilter(FilterSettings settings)
        {
            _x = new OneEuroFilter(settings);
            _y = new OneEuroFilter(settings);
        }

        public GazeSample Apply(in GazeSample s)
        {
            if (!s.Valid) return s;
            return s.WithScreen(_x.Apply(s.XPx, s.TCaptureNs), _y.Apply(s.YPx, s.TCaptureNs));
        }
    }
}
