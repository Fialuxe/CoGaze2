using System;
using CoGaze.Core;

namespace CoGaze.Expert.Sources.Fake
{
    /// <summary>
    /// A deterministic gaze trajectory for rehearsal: a slow Lissajous figure across the video
    /// rectangle, at a fixed rate, with optional constant offset (Tobii stand-in) and dropouts. Same
    /// seed and clock, same samples.
    /// </summary>
    public sealed class FakeGazeSource : IGazeSource
    {
        readonly IClock _clock;
        readonly float _cx, _cy, _ax, _ay, _offsetPx;
        readonly long _periodNs;
        readonly double _dropout;
        readonly Random _rng;
        long _nextNs = -1;

        public FakeGazeSource(string name, IClock clock, float centerXPx, float centerYPx, float amplitudeXPx,
                              float amplitudeYPx, float rateHz, int seed, float offsetPx = 0f, double dropout = 0.0)
        {
            Name = name;
            _clock = clock;
            _cx = centerXPx;
            _cy = centerYPx;
            _ax = amplitudeXPx;
            _ay = amplitudeYPx;
            _periodNs = (long)(1e9 / rateHz);
            _rng = new Random(seed);
            _offsetPx = offsetPx;
            _dropout = dropout;
        }

        public string Name { get; }

        public bool TryRead(out GazeSample sample)
        {
            long now = _clock.NowNs;
            if (_nextNs < 0) _nextNs = now;
            if (now < _nextNs)
            {
                sample = default;
                return false;
            }
            // Never fall more than a second behind: skip ahead instead of flooding.
            if (now - _nextNs > 1_000_000_000L) _nextNs = now;

            long t = _nextNs;
            _nextNs += _periodNs;
            double s = t / 1e9;
            float x = _cx + _ax * (float)Math.Sin(2 * Math.PI * 0.13 * s) + _offsetPx;
            float y = _cy + _ay * (float)Math.Sin(2 * Math.PI * 0.21 * s + 0.7) + _offsetPx;
            x += (float)(_rng.NextDouble() - 0.5) * 6f;
            y += (float)(_rng.NextDouble() - 0.5) * 6f;
            bool valid = _rng.NextDouble() >= _dropout;
            sample = new GazeSample(Name, t, now, valid, x, y, GazeMode.Eye, float.NaN, valid ? 3 : 0);
            return true;
        }
    }
}
