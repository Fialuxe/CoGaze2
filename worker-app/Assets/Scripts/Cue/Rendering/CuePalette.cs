using UnityEngine;

namespace CoGaze.Worker.Cue.Rendering
{
    /// <summary>
    /// Cue colours and line width, identical in every condition (WRK-F-025). Mirrors --cg-cue-* in
    /// com.cogaze.shared/Runtime/Theme/Resources/CoGazeTheme.uss; change both together.
    /// </summary>
    static class CuePalette
    {
        public static readonly Color Primary = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        public const float LineWidthM = 0.004f;
        public const float MarkerRadiusM = 0.008f;
        public const int CircleSegments = 72;

        /// <summary>Unlit, vertex-coloured, transparent — renders in URP as an unlit pass. One per renderer.</summary>
        public static Material NewMaterial() => new Material(Shader.Find("Sprites/Default"));
    }
}
