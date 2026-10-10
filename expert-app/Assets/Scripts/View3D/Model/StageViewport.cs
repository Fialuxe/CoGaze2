namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// The video stage rect (app.json video_rect_px: top-left origin, as UI Toolkit lays it out) as a
    /// camera pixel rect (bottom-left origin, as Camera.pixelRect and the mouse use).
    /// </summary>
    public static class StageViewport
    {
        public static (float x, float y, float w, float h) ToBottomLeft(float x, float yTop, float w, float h, float screenHeightPx) =>
            (x, screenHeightPx - yTop - h, w, h);

        public static bool Contains((float x, float y, float w, float h) r, float px, float py) =>
            px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;
    }
}
