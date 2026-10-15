using CoGaze.Core;

namespace CoGaze.Expert.App
{
    /// <summary>Stands where the WebRTC transport will go (#20): sends nowhere, receives nothing, reports disconnected.</summary>
    sealed class UnavailableTransport : ICueSender, IMessageChannel
    {
        public bool IsConnected => false;
        public void Send(in GazeCue cue) { }
        public void Send(in ControlMessage message) { }

        public bool TryReceive(out ControlMessage message)
        {
            message = default;
            return false;
        }
    }
}
