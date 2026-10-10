using UnityEngine;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// The fixed rectangle where the worker's video is shown (EXP-F-050). The video texture itself
    /// arrives with View2D (#22); until then, and in rehearsal, it is a placeholder, and in rehearsal
    /// the cue the loopback worker received is drawn on it so the whole path can be seen working.
    /// </summary>
    sealed class VideoStageView
    {
        readonly Rect _rect;
        readonly VisualElement _cue;

        public VisualElement Root { get; }

        public VideoStageView(Rect rectPx, bool rehearsal)
        {
            _rect = rectPx;
            Root = Theme.Box("cg-stage");
            Root.style.left = rectPx.x;
            Root.style.top = rectPx.y;
            Root.style.width = rectPx.width;
            Root.style.height = rectPx.height;
            Root.Add(Theme.Label(rehearsal ? "Worker video · rehearsal\nThe circle is what the worker would see"
                                           : "Worker video", "cg-stage__placeholder"));
            _cue = Theme.Box("cg-stage__cue", "cg-stage__cue--hidden");
            Root.Add(_cue);
        }

        public void ShowCue(bool valid, float u, float v, float radiusPx)
        {
            if (!valid || float.IsNaN(u))
            {
                _cue.AddToClassList("cg-stage__cue--hidden");
                return;
            }
            _cue.RemoveFromClassList("cg-stage__cue--hidden");
            float r = Mathf.Max(radiusPx, 6f);
            _cue.style.left = u * _rect.width - r;
            _cue.style.top = v * _rect.height - r;
            _cue.style.width = 2 * r;
            _cue.style.height = 2 * r;
            _cue.style.borderTopLeftRadius = r;
            _cue.style.borderTopRightRadius = r;
            _cue.style.borderBottomLeftRadius = r;
            _cue.style.borderBottomRightRadius = r;
        }
    }
}
