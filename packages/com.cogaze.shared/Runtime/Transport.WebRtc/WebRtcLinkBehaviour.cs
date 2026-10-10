#if COGAZE_WEBRTC
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CoGaze.Core;
using CoGaze.Protocol;
using Unity.WebRTC;
using UnityEngine;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// The live expert-worker link (#20, SRS 3.3): one peer connection with the worker's video, an
    /// unreliable channel for GazeCue and a reliable one for ControlMessage, negotiated through
    /// server/signaling.py. The worker offers, the expert answers. It keeps itself up: a failed or
    /// silent connection is torn down and renegotiated (EXP-N-002), and a signaling outage alone
    /// does not disturb a working connection. Created and started by an *.App bootstrap only.
    /// </summary>
    public sealed class WebRtcLinkBehaviour : MonoBehaviour, ICueSender, ICueReceiver, IMessageChannel
    {
        const long DisconnectedGraceNs = 5_000_000_000L;
        const long NegotiationTimeoutNs = 15_000_000_000L;

        readonly ConcurrentQueue<byte[]> _cuesIn = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> _controlIn = new ConcurrentQueue<byte[]>();
        readonly ControlOutbox _outbox = new ControlOutbox();
        readonly List<RTCIceCandidate> _pendingCandidates = new List<RTCIceCandidate>();
        readonly Backoff _retry = new Backoff(1000, 5000);

        LinkRole _role;
        IClock _clock;
        SignalingClient _signal;
        Texture _videoSource;
        RTCPeerConnection _pc;
        LinkChannels _channels;
        VideoStreamTrack _sendTrack;
        bool _remoteSet, _peerPresent;
        long _pcSinceNs, _badSinceNs = -1, _retryAtNs;

        /// <summary>Expert: the worker's video as last received; null until the first frame.</summary>
        public Texture ReceivedVideo { get; private set; }
        /// <summary>Last negotiation or media problem, for the checklist; null when none.</summary>
        public string Problem { get; private set; }
        public string RelayUrl => _signal?.Url;

        /// <summary>Starts signaling. videoSource is the worker's outgoing picture (IFrameTagWriter.Output); null on the expert.</summary>
        public void Begin(LinkRole role, string signalingBaseUrl, IClock clock, Texture videoSource)
        {
            _role = role;
            _clock = clock;
            _videoSource = videoSource;
            _signal = new SignalingClient(LinkRoles.UrlFor(signalingBaseUrl, role));
            _signal.Start();
            StartCoroutine(WebRTC.Update());
        }

        public LinkState State
        {
            get
            {
                if (_pc != null && _channels != null && _pc.ConnectionState == RTCPeerConnectionState.Connected && _channels.ControlOpen)
                    return LinkState.Connected;
                if (_signal == null || !_signal.IsOpen) return LinkState.SignalingDown;
                return _peerPresent ? LinkState.Connecting : LinkState.WaitingForPeer;
            }
        }

        public bool IsConnected => State == LinkState.Connected;

        public void Send(in GazeCue cue)
        {
            if (IsConnected) _channels.Send(cue);
        }

        public void Send(in ControlMessage message)
        {
            if (IsConnected) _channels.Send(MessageCodec.Encode(message));
            else _outbox.Hold(message);
        }

        public bool TryReceive(out GazeCue cue)
        {
            while (_cuesIn.TryDequeue(out var bytes))
                if (MessageCodec.TryDecode(bytes, out cue)) return true;
            cue = default;
            return false;
        }

        public bool TryReceive(out ControlMessage message)
        {
            while (_controlIn.TryDequeue(out var bytes))
                if (MessageCodec.TryDecode(bytes, out message)) return true;
            message = default;
            return false;
        }

        void Update()
        {
            if (_signal == null) return;
            long now = _clock.NowNs;
            while (_signal.TryReceive(out var m)) Handle(m);

            if (_pc != null) Watch(now);
            if (_role == LinkRole.Worker && _pc == null && _peerPresent && _signal.IsOpen && now >= _retryAtNs)
                StartCoroutine(Offer());
            if (IsConnected)
                while (_outbox.TryTake(out var held)) _channels.Send(held);
        }

        void Handle(in SignalMessage m)
        {
            switch (m.Type)
            {
                case SignalMessage.Peer:
                    _peerPresent = m.PeerPresent;
                    // A peer that left the relay may still be connected to us directly; only drop a connection that is not up.
                    if (_pc != null && _pc.ConnectionState != RTCPeerConnectionState.Connected) TearDown();
                    if (m.PeerPresent) _retryAtNs = 0;
                    break;
                case SignalMessage.Offer when _role == LinkRole.Expert:
                    TearDown();
                    StartCoroutine(Answer(m.Sdp));
                    break;
                case SignalMessage.Answer when _role == LinkRole.Worker && _pc != null && !_remoteSet:
                    StartCoroutine(AcceptAnswer(_pc, m.Sdp));
                    break;
                case SignalMessage.Candidate when _pc != null:
                    var c = new RTCIceCandidate(new RTCIceCandidateInit
                        { candidate = m.CandidateLine, sdpMid = m.SdpMid, sdpMLineIndex = m.SdpMLineIndex });
                    if (_remoteSet) _pc.AddIceCandidate(c);
                    else _pendingCandidates.Add(c);
                    break;
            }
        }

        /// <summary>Tears down a connection that failed, stayed disconnected, or never finished negotiating.</summary>
        void Watch(long now)
        {
            var s = _pc.ConnectionState;
            if (s == RTCPeerConnectionState.Connected)
            {
                _badSinceNs = -1;
                _retry.Reset();
                Problem = null;
                return;
            }
            bool failed = s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Closed;
            if (s == RTCPeerConnectionState.Disconnected && _badSinceNs < 0) _badSinceNs = now;
            bool silent = _badSinceNs >= 0 && now - _badSinceNs > DisconnectedGraceNs;
            bool stuck = s != RTCPeerConnectionState.Disconnected && now - _pcSinceNs > NegotiationTimeoutNs;
            if (!failed && !silent && !stuck) return;
            Problem = $"Connection {(failed ? "failed" : silent ? "lost" : "did not complete")}; renegotiating.";
            TearDown();
            _retryAtNs = now + _retry.NextMs() * 1_000_000L;
        }

        RTCPeerConnection Create()
        {
            var config = new RTCConfiguration { iceServers = new RTCIceServer[0] };   // same LAN: host candidates suffice
            var pc = new RTCPeerConnection(ref config);
            pc.OnIceCandidate = c => _signal.Send(SignalMessage.OfCandidate(c.Candidate, c.SdpMid, c.SdpMLineIndex ?? 0));
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack video) video.OnVideoReceived += texture => ReceivedVideo = texture;
            };
            _channels = new LinkChannels(pc, _cuesIn, _controlIn);
            _pc = pc;
            _remoteSet = false;
            _pendingCandidates.Clear();
            _pcSinceNs = _clock.NowNs;
            _badSinceNs = -1;
            return pc;
        }

        IEnumerator Offer()
        {
            var pc = Create();
            if (_videoSource != null)
            {
                _sendTrack = new VideoStreamTrack(_videoSource);
                string problem = VideoSetup.AddSendTrack(pc, _sendTrack);
                if (problem != null) Problem = problem;
            }
            var offer = pc.CreateOffer();
            yield return offer;
            if (pc != _pc) yield break;
            if (offer.IsError) { Fail($"Could not create an offer: {offer.Error.message}"); yield break; }
            var desc = offer.Desc;
            var setLocal = pc.SetLocalDescription(ref desc);
            yield return setLocal;
            if (pc != _pc) yield break;
            if (setLocal.IsError) { Fail($"Could not apply the offer: {setLocal.Error.message}"); yield break; }
            _signal.Send(SignalMessage.OfOffer(desc.sdp));
        }

        IEnumerator AcceptAnswer(RTCPeerConnection pc, string sdp)
        {
            var desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
            var op = pc.SetRemoteDescription(ref desc);
            yield return op;
            if (pc != _pc) yield break;
            if (op.IsError) { Fail($"The expert's answer was rejected: {op.Error.message}"); yield break; }
            RemoteSet();
        }

        IEnumerator Answer(string sdp)
        {
            var pc = Create();
            var remote = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
            var setRemote = pc.SetRemoteDescription(ref remote);
            yield return setRemote;
            if (pc != _pc) yield break;
            if (setRemote.IsError) { Fail($"The worker's offer was rejected: {setRemote.Error.message}"); yield break; }
            RemoteSet();
            var answer = pc.CreateAnswer();
            yield return answer;
            if (pc != _pc) yield break;
            if (answer.IsError) { Fail($"Could not create an answer: {answer.Error.message}"); yield break; }
            var desc = answer.Desc;
            var setLocal = pc.SetLocalDescription(ref desc);
            yield return setLocal;
            if (pc != _pc) yield break;
            if (setLocal.IsError) { Fail($"Could not apply the answer: {setLocal.Error.message}"); yield break; }
            _signal.Send(SignalMessage.OfAnswer(desc.sdp));
        }

        void RemoteSet()
        {
            _remoteSet = true;
            foreach (var c in _pendingCandidates) _pc.AddIceCandidate(c);
            _pendingCandidates.Clear();
        }

        void Fail(string problem)
        {
            Problem = problem;
            Debug.LogWarning($"[WebRtc] {problem}");
            TearDown();
            _retryAtNs = _clock.NowNs + _retry.NextMs() * 1_000_000L;
        }

        void TearDown()
        {
            _channels?.Dispose();
            _channels = null;
            if (_pc != null)
            {
                _pc.OnIceCandidate = null;
                _pc.OnTrack = null;
                _pc.Close();
                _pc.Dispose();
                _pc = null;
            }
            _sendTrack?.Dispose();
            _sendTrack = null;
            ReceivedVideo = null;
            _remoteSet = false;
            _pendingCandidates.Clear();
        }

        void OnDestroy()
        {
            TearDown();
            _signal?.Dispose();
        }
    }
}
#endif
