using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Reads the frame-id strip (<see cref="FrameIdStrip"/>) off the bottom rows of the received
    /// texture with an asynchronous GPU readback each displayed frame. The readback is queued at the
    /// point in the frame where the texture holds what is drawn, so the id belongs to that frame;
    /// the shown time is taken when the request is made, not when the bytes arrive. Id 0 means the
    /// worker had no camera image yet and is skipped.
    /// </summary>
    public sealed class StripFrameTagReader : IFrameTagReader
    {
        const int MaxInFlight = 6;

        readonly Queue<(long t, long id)> _ready = new Queue<(long, long)>();
        byte[] _buffer = new byte[0];
        int _inFlight;
        long _lastId = -1;

        public long Decoded { get; private set; }
        public long Failed { get; private set; }
        public long Frames { get; private set; }

        public RectInt ImageRect(Texture received) =>
            received == null ? new RectInt() : new RectInt(0, 0, received.width, Mathf.Max(0, received.height - FrameIdStrip.HeightPx));

        public void Read(Texture received, long tShownNs)
        {
            if (received == null || received.height <= FrameIdStrip.HeightPx || !FrameIdStrip.Fits(received.width)) return;
            if (_inFlight >= MaxInFlight) return;
            _inFlight++;
            int width = received.width;
            // Texture space has its origin bottom-left: the strip (below the image as displayed) is rows 0..HeightPx.
            AsyncGPUReadback.Request(received, 0, 0, width, 0, FrameIdStrip.HeightPx, 0, 1, TextureFormat.RGBA32,
                                     r => OnReadback(r, width, tShownNs));
        }

        public bool TryTake(out long tShownNs, out long videoFrameId)
        {
            if (_ready.Count == 0)
            {
                tShownNs = 0;
                videoFrameId = 0;
                return false;
            }
            (tShownNs, videoFrameId) = _ready.Dequeue();
            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest r, int width, long tShownNs)
        {
            _inFlight--;
            if (r.hasError)
            {
                Failed++;
                return;
            }
            var data = r.GetData<byte>();
            if (_buffer.Length != data.Length) _buffer = new byte[data.Length];
            data.CopyTo(_buffer);
            if (!FrameIdStrip.TryRead(_buffer, width, FrameIdStrip.HeightPx, out uint id))
            {
                Failed++;
                return;
            }
            Decoded++;
            if (id == 0 || id == _lastId) return;
            _lastId = id;
            Frames++;
            _ready.Enqueue((tShownNs, id));
        }
    }
}
