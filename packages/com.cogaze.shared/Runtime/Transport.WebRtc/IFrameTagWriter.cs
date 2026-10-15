using UnityEngine;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Worker half of the TBD-04 seam: puts a video_frame_id into the picture that is streamed.
    /// <see cref="Output"/> is the texture the video track sends; <see cref="Write"/> fills it with a
    /// camera image and that image's id. Swapping the id mechanism means another implementation of
    /// this and <see cref="IFrameTagReader"/>, nothing else.
    /// </summary>
    public interface IFrameTagWriter
    {
        Texture Output { get; }
        void Write(Texture cameraImage, long videoFrameId);
    }
}
