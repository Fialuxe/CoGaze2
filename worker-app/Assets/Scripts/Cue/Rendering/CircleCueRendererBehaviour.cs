using CoGaze.Core;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze.Worker.Cue.Rendering
{
    /// <summary>Circle: an outline on the work surface centred on the hit (WRK-F-022, outline only per WRK-F-024).</summary>
    public sealed class CircleCueRendererBehaviour : MonoBehaviour, ICueRenderer
    {
        LineRenderer _ring;

        Material _material;

        public Visualization Kind => Visualization.Circle;

        void Awake()
        {
            _material = CuePalette.NewMaterial();
            _ring = CueLines.Create(transform, "Ring", loop: true, _material);
        }

        public void Show(in CueVisual v)
        {
            CueLines.Circle(_ring, v.Hit.ToUnity(), v.Normal.ToUnity(), v.RadiusM);
            CueLines.SetOpacity(_ring, v.Opacity);
            _ring.enabled = true;
        }

        public void Hide() => _ring.enabled = false;
    }
}
