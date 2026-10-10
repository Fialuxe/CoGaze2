using System;
using CoGaze.Core;

namespace CoGaze.Worker.Replay
{
    /// <summary>
    /// Gaze cues on a slow figure-of-eight over the video, with sigma breathing between two values,
    /// at a fixed rate. Feeds the live display path in the editor, so renderers can be judged without
    /// an expert. Recorded-series replay (WRK-F-040, 041) is #35.
    /// </summary>
    public sealed class SyntheticCueReceiver : ICueReceiver
    {
        readonly IClock _clock;
        readonly Func<long> _latestFrameId;
        readonly long _periodNs;
        long _nextNs;
        uint _seq;

        public SyntheticCueReceiver(IClock clock, Func<long> latestFrameId, float rateHz = 30f)
        {
            _clock = clock;
            _latestFrameId = latestFrameId;
            _periodNs = (long)(1e9 / rateHz);
        }

        public bool TryReceive(out GazeCue cue)
        {
            long now = _clock.NowNs;
            if (now < _nextNs)
            {
                cue = default;
                return false;
            }
            _nextNs = now + _periodNs;
            double t = now / 1e9;
            float u = 0.5f + 0.25f * (float)Math.Sin(t * 0.6);
            float v = 0.6f + 0.15f * (float)Math.Sin(t * 1.2);
            float sigma = 2.5f + 1.5f * (float)Math.Sin(t * 0.35);
            cue = new GazeCue(++_seq, "replay", true, GazeMode.Eye, u, v, _latestFrameId(), sigma, now);
            return true;
        }
    }
}
