namespace CoGaze.Transport.WebRtc
{
    /// <summary>How far the link to the other app has come, worst first.</summary>
    public enum LinkState
    {
        /// <summary>The signaling relay cannot be reached.</summary>
        SignalingDown,
        /// <summary>Relay reached; the other app is not connected to it.</summary>
        WaitingForPeer,
        /// <summary>Both apps on the relay; offer/answer/ICE in progress or the channels not yet open.</summary>
        Connecting,
        /// <summary>Peer connection up and the control channel open.</summary>
        Connected,
    }
}
