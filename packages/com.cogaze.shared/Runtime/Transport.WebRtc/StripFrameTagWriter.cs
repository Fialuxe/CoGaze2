#if COGAZE_WEBRTC
using System;
using Unity.WebRTC;
using UnityEngine;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// Composes the streamed picture: the camera image on top, scaled to a fixed size, and the
    /// frame-id strip in the <see cref="FrameIdStrip.HeightPx"/> rows below it. Normalised (u, v)
    /// of the image part are unchanged by the scaling, which is all the cue path uses. Call Write at
    /// end of frame, when the camera texture holds the image its pose belongs to. Until the first
    /// Write the output is black with id 0 ("no frame").
    /// </summary>
    public sealed class StripFrameTagWriter : IFrameTagWriter, IDisposable
    {
        readonly RenderTexture _output;
        readonly Texture2D _strip;
        readonly byte[] _pixels;
        readonly int _width, _imageHeight;

        public StripFrameTagWriter(int imageWidthPx, int imageHeightPx)
        {
            if (!FrameIdStrip.Fits(imageWidthPx))
                throw new ArgumentException($"Video width {imageWidthPx} px is too narrow for the frame-id strip.");
            _width = imageWidthPx;
            _imageHeight = imageHeightPx;
            var format = WebRTC.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
            _output = new RenderTexture(imageWidthPx, imageHeightPx + FrameIdStrip.HeightPx, 0, format) { name = "CoGaze video" };
            _output.Create();
            _strip = new Texture2D(imageWidthPx, FrameIdStrip.HeightPx, TextureFormat.RGBA32, false, false)
                { filterMode = FilterMode.Point, name = "CoGaze frame id" };
            _pixels = new byte[imageWidthPx * FrameIdStrip.HeightPx * 4];
            Draw(null, 0);
        }

        public Texture Output => _output;

        public void Write(Texture cameraImage, long videoFrameId) => Draw(cameraImage, (uint)videoFrameId);

        void Draw(Texture image, uint id)
        {
            FrameIdStrip.Paint(id, _pixels, _width);
            _strip.SetPixelData(_pixels, 0);
            _strip.Apply(false);

            var previous = RenderTexture.active;
            RenderTexture.active = _output;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, _width, _imageHeight + FrameIdStrip.HeightPx, 0);   // GUI-style: y = 0 is the top row
            if (image != null) Graphics.DrawTexture(new Rect(0, 0, _width, _imageHeight), image);
            else GL.Clear(false, true, Color.black);
            Graphics.DrawTexture(new Rect(0, _imageHeight, _width, FrameIdStrip.HeightPx), _strip);
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        public void Dispose()
        {
            _output.Release();
            UnityEngine.Object.Destroy(_output);
            UnityEngine.Object.Destroy(_strip);
        }
    }
}
#endif
