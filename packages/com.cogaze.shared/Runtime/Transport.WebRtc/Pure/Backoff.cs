using System;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>Reconnect delays that double from FirstMs up to MaxMs, back to FirstMs after a success.</summary>
    public sealed class Backoff
    {
        readonly int _firstMs, _maxMs;
        int _nextMs;

        public Backoff(int firstMs = 500, int maxMs = 5000)
        {
            _firstMs = firstMs;
            _maxMs = maxMs;
            _nextMs = firstMs;
        }

        public int NextMs()
        {
            int d = _nextMs;
            _nextMs = Math.Min(_maxMs, _nextMs * 2);
            return d;
        }

        public void Reset() => _nextMs = _firstMs;
    }
}
