using System;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>
    /// One-Euro filter for one axis (Casiez et al., CHI 2012): a low-pass whose cutoff rises with
    /// speed, so fixations are steady and saccades are not lagged.
    /// </summary>
    public sealed class OneEuroFilter
    {
        readonly FilterSettings _s;
        bool _has;
        double _x, _dx;
        long _tNs;

        public OneEuroFilter(FilterSettings settings)
        {
            _s = settings;
        }

        public void Reset() => _has = false;

        public float Apply(float value, long tNs)
        {
            if (!_s.Enabled) return value;
            if (!_has || tNs <= _tNs)
            {
                if (!_has) { _x = value; _dx = 0; _tNs = tNs; _has = true; }
                return (float)_x;
            }
            double dt = (tNs - _tNs) / 1e9;
            _tNs = tNs;
            double dx = (value - _x) / dt;
            _dx = Lerp(_dx, dx, Alpha(_s.DCutoffHz, dt));
            double cutoff = _s.MinCutoffHz + _s.Beta * Math.Abs(_dx);
            _x = Lerp(_x, value, Alpha(cutoff, dt));
            return (float)_x;
        }

        static double Alpha(double cutoffHz, double dt)
        {
            double tau = 1.0 / (2 * Math.PI * cutoffHz);
            return 1.0 / (1.0 + tau / dt);
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
