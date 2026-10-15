using UnityEngine;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Expert half of the TBD-04 seam: given the received texture each frame it is shown, yields
    /// (time shown, video_frame_id) once per distinct frame, for DisplayedFrameLog.Record.
    /// </summary>
    public interface IFrameTagReader
    {
        /// <summary>Called once per displayed frame with the texture on screen and the time it is shown.</summary>
        void Read(Texture received, long tShownNs);
        bool TryTake(out long tShownNs, out long videoFrameId);
        /// <summary>Part of the received texture that is camera image, in pixels, top-left origin.</summary>
        RectInt ImageRect(Texture received);
        long Decoded { get; }
        long Failed { get; }
        long Frames { get; }
    }
}
