using CoGaze.Core;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Reports the link on the checklist and records every outage in events.csv (EXP-N-002): a
    /// link_down row when it drops and a link_up row with the gap length when it returns, so the
    /// analysis can mark the interval in which no gaze or video crossed. The health board is
    /// optional (the worker has none).
    /// </summary>
    public sealed class LinkMonitor
    {
        public const string EventDown = "link_down";
        public const string EventUp = "link_up";
        public const string HealthKey = "link";

        readonly ILogSink _log;
        readonly IHealthBoard _health;
        readonly string _peer, _relayUrl;
        bool _haveState, _up, _everUp;
        LinkState _state;
        long _downSinceNs;

        public LinkMonitor(ILogSink log, IHealthBoard health, LinkRole role, string relayUrl)
        {
            _log = log;
            _health = health;
            _peer = LinkRoles.PeerOf(role);
            _relayUrl = relayUrl;
        }

        public void Update(LinkState state, long nowNs)
        {
            if (_haveState && state == _state) return;
            bool up = state == LinkState.Connected;
            if (up && !_up)
                _log.Event(nowNs, EventUp, _everUp ? $"gap_ms={(nowNs - _downSinceNs) / 1_000_000}" : "first");
            else if (!up && _up)
            {
                _downSinceNs = nowNs;
                _log.Event(nowNs, EventDown, $"state={state}");
            }
            _everUp |= up;
            _up = up;
            _state = state;
            _haveState = true;
            _health?.Set(Item(state));
        }

        HealthItem Item(LinkState s)
        {
            string label = $"Link to {_peer}";
            switch (s)
            {
                case LinkState.Connected:
                    return new HealthItem(HealthKey, label, HealthLevel.Ok, "connected");
                case LinkState.Connecting:
                    return new HealthItem(HealthKey, label, HealthLevel.Warning, "connecting",
                        $"Negotiating the WebRTC connection with the {_peer}.",
                        "Wait a few seconds; if it stays, check both devices are on the same network.");
                case LinkState.WaitingForPeer:
                    return new HealthItem(HealthKey, label, HealthLevel.Error, $"no {_peer}",
                        $"The {_peer} app is not connected to the signaling relay.",
                        $"Start the {_peer} app and check it points at {_relayUrl}.");
                default:
                    return new HealthItem(HealthKey, label, HealthLevel.Error, "no relay",
                        $"Cannot reach the signaling relay at {_relayUrl}.",
                        "Start server/service.py (or server/signaling.py) and check network.signaling_url.");
            }
        }
    }
}
