using System;

namespace CoGaze.Core
{
    /// <summary>
    /// The expert monitor as seen from the expert's eyes: converts between screen pixels and visual
    /// angle. Small-angle form around the screen normal, adequate for error and sigma magnitudes.
    /// </summary>
    public readonly struct ScreenGeometry
    {
        public readonly float PxPerMm;
        public readonly float ViewingDistanceMm;

        public ScreenGeometry(int widthPx, float widthMm, float viewingDistanceMm)
        {
            PxPerMm = widthPx / widthMm;
            ViewingDistanceMm = viewingDistanceMm;
        }

        /// <summary>Pixels subtended by one degree at the viewing distance.</summary>
        public float PxPerDeg => (float)(ViewingDistanceMm * Math.Tan(Math.PI / 180.0)) * PxPerMm;

        public float PxToDeg(float px) => (float)(Math.Atan(px / PxPerMm / ViewingDistanceMm) * 180.0 / Math.PI);
    }
}
