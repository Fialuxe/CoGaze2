#if COGAZE_WEBRTC
using System.Linq;
using Unity.WebRTC;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>Worker video track settings: send only, H.264 (WRK-F-010), bitrate and rate caps (TBD-10).</summary>
    static class VideoSetup
    {
        public const ulong MaxBitrateBps = 8_000_000;
        public const uint MaxFramerate = 30;

        /// <summary>Adds the track; returns a problem to report, or null.</summary>
        public static string AddSendTrack(RTCPeerConnection pc, VideoStreamTrack track)
        {
            var transceiver = pc.AddTransceiver(track, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.SendOnly });
            string problem = null;

            var h264 = RTCRtpSender.GetCapabilities(TrackKind.Video).codecs
                .Where(c => c.mimeType == "video/H264").ToArray();
            if (h264.Length == 0) problem = "This device offers no H.264 encoder; WebRTC will pick another codec.";
            else if (transceiver.SetCodecPreferences(h264) != RTCErrorType.None) problem = "Could not prefer H.264.";

            var parameters = transceiver.Sender.GetParameters();
            foreach (var e in parameters.encodings)
            {
                e.maxBitrate = MaxBitrateBps;
                e.maxFramerate = MaxFramerate;
            }
            var error = transceiver.Sender.SetParameters(parameters);
            if (error.errorType != RTCErrorType.None) problem = (problem ?? "") + $" Bitrate cap not applied: {error.message}";
            return problem;
        }
    }
}
#endif
