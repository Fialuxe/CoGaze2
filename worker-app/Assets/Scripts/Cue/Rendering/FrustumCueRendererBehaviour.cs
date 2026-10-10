using CoGaze.Core;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze.Worker.Cue.Rendering
{
    /// <summary>
    /// Frustum: a cone from the origin opening onto the surface with half-angle sigma (WRK-F-022, 023),
    /// drawn as its base circle and a few generator lines so it never hides the objects underneath.
    /// </summary>
    public sealed class FrustumCueRendererBehaviour : MonoBehaviour, ICueRenderer
    {
        const int Generators = 6;
        LineRenderer _base;
        readonly LineRenderer[] _edges = new LineRenderer[Generators];

        Material _material;

        public Visualization Kind => Visualization.Frustum;

        void Awake()
        {
            _material = CuePalette.NewMaterial();
            _base = CueLines.Create(transform, "Base", loop: true, _material);
            for (int i = 0; i < Generators; i++) _edges[i] = CueLines.Create(transform, "Edge" + i, loop: false, _material);
        }

        public void Show(in CueVisual v)
        {
            Vector3 origin = v.Origin.ToUnity(), hit = v.Hit.ToUnity(), normal = v.Normal.ToUnity();
            CueLines.Circle(_base, hit, normal, v.RadiusM);
            CueLines.SetOpacity(_base, v.Opacity);
            _base.enabled = true;
            int step = CuePalette.CircleSegments / Generators;
            for (int i = 0; i < Generators; i++)
            {
                var e = _edges[i];
                e.positionCount = 2;
                e.SetPosition(0, origin);
                e.SetPosition(1, _base.GetPosition(i * step));
                CueLines.SetOpacity(e, v.Opacity * 0.6f);
                e.enabled = true;
            }
        }

        public void Hide()
        {
            _base.enabled = false;
            foreach (var e in _edges) e.enabled = false;
        }
    }
}
