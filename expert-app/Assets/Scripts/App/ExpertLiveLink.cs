#if COGAZE_WEBRTC
using CoGaze.Core;
using CoGaze.Expert.UI;
using CoGaze.Expert.View2D;
using CoGaze.Transport.WebRtc;
using UnityEngine;

namespace CoGaze.Expert.App
{
    /// <summary>
    /// Wiring of the live link on the expert, once per frame: link state to the checklist and
    /// events.csv, the received video into the stage, fitted to its aspect (#22), and the id of
    /// every distinct frame shown into DisplayedFrameLog (EXP-F-023, TBD-04).
    /// </summary>
    sealed class ExpertLiveLink
    {
        readonly WebRtcLinkBehaviour _link;
        readonly IFrameTagReader _reader = new StripFrameTagReader();
        readonly LinkMonitor _monitor;
        readonly VideoHealthReporter _videoHealth;
        readonly IClock _clock;
        readonly VideoRect _stage;
        ExpertScreenBehaviour _screen;
        View2DProjector _projector;
        DisplayedFrameLog _frames;
        int _width, _height;

        public ExpertLiveLink(GameObject host, string signalingUrl, IClock clock, ILogSink log, IHealthBoard health, VideoRect stage)
        {
            _clock = clock;
            _stage = stage;
            _link = host.AddComponent<WebRtcLinkBehaviour>();
            _link.Begin(LinkRole.Expert, signalingUrl, clock, null);
            _monitor = new LinkMonitor(log, health, LinkRole.Expert, _link.RelayUrl);
            _videoHealth = new VideoHealthReporter(health);
        }

        public WebRtcLinkBehaviour Link => _link;

        public void Attach(ExpertScreenBehaviour screen, View2DProjector projector, DisplayedFrameLog frames)
        {
            _screen = screen;
            _projector = projector;
            _frames = frames;
        }

        public void Tick()
        {
            long now = _clock.NowNs;
            _monitor.Update(_link.State, now);
            var texture = _link.ReceivedVideo;
            if (texture != null)
            {
                _reader.Read(texture, now);
                var image = _reader.ImageRect(texture);
                if (image.width != _width || image.height != _height)
                {
                    _width = image.width;
                    _height = image.height;
                    _projector?.SetRect(_stage.FitInside(_width, _height));
                }
                var fit = _stage.FitInside(_width, _height);
                _screen?.ShowVideo(texture, new Rect(image.x, image.y, image.width, image.height),
                                   new Rect(fit.XPx, fit.YPx, fit.WidthPx, fit.HeightPx));
            }
            else _screen?.ShowVideo(null, default, default);

            while (_reader.TryTake(out long tShown, out long id)) _frames?.Record(tShown, id);
            _videoHealth.Tick(texture != null, _reader.Decoded, _reader.Failed, _reader.Frames, now);
        }
    }
}
#endif
