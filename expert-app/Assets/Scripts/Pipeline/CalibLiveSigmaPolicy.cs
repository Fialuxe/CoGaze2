using System;
using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>
    /// sigma_source = calib_live: calibration error combined with the recent dispersion of the gaze
    /// and the source's confidence.
    /// PROVISIONAL (TBD-06, #31): sigma = sqrt(calib² + dispersion²) × confidence factor, where the
    /// factor is 1 at confidence 3 (or unknown) and grows as confidence falls. Replace once the Tobii
    /// comparison has fixed the composition.
    /// </summary>
    public sealed class CalibLiveSigmaPolicy : ISigmaPolicy
    {
        readonly CalibSigmaPolicy _calib;
        readonly ScreenGeometry _screen;
        readonly float[] _xs, _ys;
        int _count, _next;

        public CalibLiveSigmaPolicy(ICalibrationState calibration, float defaultDeg, ScreenGeometry screen, int window)
        {
            _calib = new CalibSigmaPolicy(calibration, defaultDeg);
            _screen = screen;
            _xs = new float[window];
            _ys = new float[window];
        }

        public float SigmaDeg(in GazeSample sample, in GazeRay ray)
        {
            float calib = _calib.SigmaDeg(sample, ray);
            if (sample.Valid)
            {
                _xs[_next] = sample.XPx;
                _ys[_next] = sample.YPx;
                _next = (_next + 1) % _xs.Length;
                _count = Math.Min(_count + 1, _xs.Length);
            }
            float dispersion = _screen.PxToDeg(RmsDistancePx());
            return (float)Math.Sqrt(calib * calib + dispersion * dispersion) * ConfidenceFactor(sample.Confidence);
        }

        float RmsDistancePx()
        {
            if (_count < 2) return 0f;
            double mx = 0, my = 0;
            for (int i = 0; i < _count; i++) { mx += _xs[i]; my += _ys[i]; }
            mx /= _count;
            my /= _count;
            double sum = 0;
            for (int i = 0; i < _count; i++)
                sum += (_xs[i] - mx) * (_xs[i] - mx) + (_ys[i] - my) * (_ys[i] - my);
            return (float)Math.Sqrt(sum / _count);
        }

        static float ConfidenceFactor(int confidence)
        {
            switch (confidence)
            {
                case 2: return 1.25f;
                case 1: return 1.5f;
                case 0: return 2f;
                default: return 1f;   // 3, or a source without confidence (-1)
            }
        }
    }
}
