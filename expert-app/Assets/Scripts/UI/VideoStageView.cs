using CoGaze.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// The fixed rectangle where the worker's video is shown (EXP-F-050). The video texture arrives
    /// with View2D (#22). In rehearsal the stage draws what the worker would see for the current
    /// condition — Ray, Circle or Frustum, at the size the worker gets — and a label always says what
    /// is being shown, so "nothing visible" is never ambiguous.
    /// </summary>
    sealed class VideoStageView
    {
        static readonly Color Cue = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        readonly Rect _rect;
        readonly IConditionSource _conditions;
        readonly VisualElement _canvas;
        readonly Label _label;
        bool _visible;
        Vector2 _point;
        float _radius;

        public VisualElement Root { get; }

        public VideoStageView(Rect rectPx, bool rehearsal, IConditionSource conditions)
        {
            _rect = rectPx;
            _conditions = conditions;
            Root = Theme.Box("cg-stage");
            Root.style.left = rectPx.x;
            Root.style.top = rectPx.y;
            Root.style.width = rectPx.width;
            Root.style.height = rectPx.height;
            Root.Add(Theme.Label(rehearsal ? "Worker video (rehearsal: simulated)" : "Worker video", "cg-stage__placeholder"));

            _canvas = Theme.Box("cg-screen");
            _canvas.pickingMode = PickingMode.Ignore;
            _canvas.generateVisualContent += Draw;
            Root.Add(_canvas);

            _label = Theme.Label("", "cg-stage__label");
            Root.Add(_label);
        }

        /// <summary>Where the cue sits on the video (u, v) and its radius in stage px.</summary>
        public void ShowCue(bool valid, float u, float v, float radiusPx)
        {
            _visible = valid && !float.IsNaN(u);
            _point = new Vector2(u * _rect.width, v * _rect.height);
            _radius = Mathf.Max(radiusPx, 6f);
            _label.text = LabelText();
            _canvas.MarkDirtyRepaint();
        }

        string LabelText()
        {
            if (!_conditions.HasCondition) return "WORKER SEES  ·  nothing yet — no condition sent";
            var c = _conditions.Current;
            if (!c.SendsGaze) return $"WORKER SEES  ·  no gaze ({c.ConditionId} is a no-gaze condition)";
            string vis = Describe.Vis(c.Visualization).ToUpperInvariant();
            if (!_visible) return $"WORKER SEES  ·  {vis} — hidden: gaze is off the video or not tracked";
            return $"WORKER SEES  ·  {vis}  ·  {(c.ShowUncertainty ? "size follows uncertainty" : "fixed size")}";
        }

        /// <summary>The cue origin for Ray/Frustum in the preview: bottom centre, where the worker's head is (TBD-11).</summary>
        Vector2 Origin => new Vector2(_rect.width / 2f, _rect.height + 40f);

        void Draw(MeshGenerationContext mgc)
        {
            if (!_visible || !_conditions.HasCondition || !_conditions.Current.SendsGaze) return;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            switch (_conditions.Current.Visualization)
            {
                case Visualization.Circle:
                    Circle(p, _radius, fill: 0.10f);
                    break;
                case Visualization.Ray:
                    Line(p, Origin, _point, 3f, 0.9f);
                    Circle(p, 7f, fill: 1f);
                    break;
                case Visualization.Frustum:
                    // A cone seen from above: a filled wedge from the origin to the circle's edges.
                    var dir = (_point - Origin).normalized;
                    var side = new Vector2(-dir.y, dir.x) * _radius;
                    p.fillColor = WithAlpha(0.14f);
                    p.BeginPath();
                    p.MoveTo(Origin);
                    p.LineTo(_point + side);
                    p.LineTo(_point - side);
                    p.ClosePath();
                    p.Fill();
                    Line(p, Origin, _point + side, 2f, 0.85f);
                    Line(p, Origin, _point - side, 2f, 0.85f);
                    Circle(p, _radius, fill: 0.10f);
                    break;
            }
        }

        void Circle(Painter2D p, float r, float fill)
        {
            p.BeginPath();
            p.Arc(_point, r, Angle.Degrees(0), Angle.Degrees(360));
            p.fillColor = WithAlpha(fill);
            p.Fill();
            p.strokeColor = Cue;
            p.lineWidth = 3f;
            p.Stroke();
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b, float width, float alpha)
        {
            p.strokeColor = WithAlpha(alpha);
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        static Color WithAlpha(float a) => new Color(Cue.r, Cue.g, Cue.b, a);
    }
}
