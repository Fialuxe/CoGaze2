using CoGaze.Core;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze.Worker.Cue.Rendering
{
    /// <summary>Ray: a line from the origin to the hit and a small mark at the hit (WRK-F-022). Origin is TBD-11 (#25).</summary>
    public sealed class RayCueRendererBehaviour : MonoBehaviour, ICueRenderer
    {
        LineRenderer _line, _mark;

        Material _material;

        public Visualization Kind => Visualization.Ray;

        void Awake()
        {
            _material = CuePalette.NewMaterial();
            _line = CueLines.Create(transform, "Line", loop: false, _material);
            _mark = CueLines.Create(transform, "Mark", loop: true, _material);
        }

        public void Show(in CueVisual v)
        {
            var hit = v.Hit.ToUnity();
            _line.positionCount = 2;
            _line.SetPosition(0, v.Origin.ToUnity());
            _line.SetPosition(1, hit);
            CueLines.Circle(_mark, hit, v.Normal.ToUnity(), CuePalette.MarkerRadiusM);
            CueLines.SetOpacity(_line, v.Opacity);
            CueLines.SetOpacity(_mark, v.Opacity);
            _line.enabled = _mark.enabled = true;
        }

        public void Hide() => _line.enabled = _mark.enabled = false;
    }
}
