namespace CoGaze.Transport.WebRtc
{
    /// <summary>Which end of the link this app is. The worker sends video and makes the offer.</summary>
    public enum LinkRole
    {
        Expert,
        Worker,
    }
}
