namespace CoGaze.Expert.View3D.Model
{
    /// <summary>Where the reconstruction source is in its life.</summary>
    public enum LinkState
    {
        Idle,
        Connecting,
        Connected,
        Replaying,
        ReplayDone,
        /// <summary>Could not connect, the connection dropped, or there is nothing to replay; see the problem text.</summary>
        Failed,
    }
}
