using CoGaze.Core;

namespace CoGaze.Worker.Session
{
    /// <summary>
    /// WorkerStatus at 1 Hz (WRK-F-037): the app's measured frame rate over the last second
    /// (WRK-N-001 asks for 72 fps), the alignment state and whether a session is being recorded.
    /// Call Tick once per rendered frame.
    /// </summary>
    public sealed class StatusReporter
    {
        const long PeriodNs = 1_000_000_000L;

        readonly IMessageChannel _channel;
        readonly IClock _clock;
        long _nextNs = long.MinValue, _windowStartNs;
        int _frames;

        public StatusReporter(IMessageChannel channel, IClock clock)
        {
            _channel = channel;
            _clock = clock;
        }

        public void Tick(AlignmentState alignment, bool recording)
        {
            long now = _clock.NowNs;
            if (_nextNs == long.MinValue)
            {
                _windowStartNs = now;
                _nextNs = now;
            }
            if (now < _nextNs)
            {
                _frames++;
                return;
            }
            float fps = now > _windowStartNs ? (float)(_frames * 1e9 / (now - _windowStartNs)) : 0f;
            _channel.Send(ControlMessage.Of(new WorkerStatus(fps, alignment, recording, now)));
            _frames = 1;
            _windowStartNs = now;
            _nextNs = now + PeriodNs;
        }
    }
}
