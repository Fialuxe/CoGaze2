namespace CoGaze.Expert.View2D
{
    /// <summary>Where the worker video is drawn on the expert monitor, physical px, top-left origin (EXP-F-050).</summary>
    public readonly struct VideoRect
    {
        public readonly float XPx, YPx, WidthPx, HeightPx;

        public VideoRect(float xPx, float yPx, float widthPx, float heightPx)
        {
            XPx = xPx;
            YPx = yPx;
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        /// <summary>Screen px → normalised video coordinates (COM-F-007). False outside the video (EXP-F-022).</summary>
        public bool TryToUv(float xPx, float yPx, out float u, out float v)
        {
            u = (xPx - XPx) / WidthPx;
            v = (yPx - YPx) / HeightPx;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        /// <summary>
        /// The largest rect of the image's aspect centred inside this one (EXP-F-050: aspect kept,
        /// same stage for every condition). This rect unchanged when the image size is not known yet.
        /// </summary>
        public VideoRect FitInside(float imageWidthPx, float imageHeightPx)
        {
            if (imageWidthPx <= 0f || imageHeightPx <= 0f) return this;
            float scale = System.Math.Min(WidthPx / imageWidthPx, HeightPx / imageHeightPx);
            float w = imageWidthPx * scale, h = imageHeightPx * scale;
            return new VideoRect(XPx + (WidthPx - w) / 2f, YPx + (HeightPx - h) / 2f, w, h);
        }
    }
}
