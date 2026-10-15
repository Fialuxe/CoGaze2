namespace CoGaze.Transport.WebRtc
{
    public static class LinkRoles
    {
        /// <summary>Path segment of the relay route, /signal/{expert,worker}.</summary>
        public static string PathOf(LinkRole role) => role == LinkRole.Worker ? "worker" : "expert";

        /// <summary>The other end's name, for messages ("the worker is not connected").</summary>
        public static string PeerOf(LinkRole role) => role == LinkRole.Worker ? "expert" : "worker";

        /// <summary>The relay URL for a role: the configured base (".../signal") plus the role.</summary>
        public static string UrlFor(string signalingBaseUrl, LinkRole role) =>
            signalingBaseUrl.TrimEnd('/') + "/" + PathOf(role);
    }
}
