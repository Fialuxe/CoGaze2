#if COGAZE_WEBRTC
using System;
using System.Collections.Concurrent;
using CoGaze.Core;
using CoGaze.Protocol;
using Unity.WebRTC;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// The two data channels of one peer connection (SRS 3.3): "cue", unordered with no
    /// retransmits, for GazeCue; "control", ordered and reliable, for everything else. Both are
    /// pre-negotiated with fixed ids so either side can create them without waiting for the other.
    /// Bytes are MessageCodec, the format rehearsal already exercises through LoopbackTransport.
    /// </summary>
    sealed class LinkChannels : IDisposable
    {
        readonly RTCDataChannel _cue, _control;
        readonly ConcurrentQueue<byte[]> _cuesIn, _controlIn;

        public LinkChannels(RTCPeerConnection pc, ConcurrentQueue<byte[]> cuesIn, ConcurrentQueue<byte[]> controlIn)
        {
            _cuesIn = cuesIn;
            _controlIn = controlIn;
            _cue = pc.CreateDataChannel("cue", new RTCDataChannelInit { negotiated = true, id = 0, ordered = false, maxRetransmits = 0 });
            _control = pc.CreateDataChannel("control", new RTCDataChannelInit { negotiated = true, id = 1, ordered = true });
            _cue.OnMessage = bytes => Enqueue(_cuesIn, bytes);
            _control.OnMessage = bytes => Enqueue(_controlIn, bytes);
        }

        public bool ControlOpen => _control.ReadyState == RTCDataChannelState.Open;

        public void Send(in GazeCue cue)
        {
            if (_cue.ReadyState == RTCDataChannelState.Open) _cue.Send(MessageCodec.Encode(cue));
        }

        public void Send(byte[] controlBytes) => _control.Send(controlBytes);

        static void Enqueue(ConcurrentQueue<byte[]> queue, byte[] bytes)
        {
            // A peer that floods should not grow memory without bound; the newest messages matter most.
            if (queue.Count > 1024) queue.TryDequeue(out _);
            queue.Enqueue(bytes);
        }

        public void Dispose()
        {
            _cue.OnMessage = null;
            _control.OnMessage = null;
            _cue.Close();
            _control.Close();
            _cue.Dispose();
            _control.Dispose();
        }
    }
}
#endif
