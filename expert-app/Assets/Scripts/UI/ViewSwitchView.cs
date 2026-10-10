using System;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// Switches the stage between the worker's video (2D) and the reconstruction (3D): a segmented
    /// control in the top bar and the V key. In 3D the screen and stage backgrounds turn transparent so
    /// the 3D camera, which renders into the stage rect, shows through; the rest of the screen is
    /// unchanged. A label on the stage says what the 3D view shows, and a key strip says how to steer.
    /// </summary>
    sealed class ViewSwitchView
    {
        const float LabelRefreshS = 0.25f;

        readonly VisualElement _screen, _stage;
        readonly Button _button2d, _button3d;
        readonly Label _label;
        readonly Action<bool> _onChanged;
        readonly Func<string> _label3d;
        bool _is3d;
        float _nextLabel;

        /// <param name="onChanged">True when 3D is now shown.</param>
        /// <param name="label3d">What the 3D view shows, for the stage label.</param>
        public ViewSwitchView(VisualElement stage, VisualElement bar, VisualElement keys, bool start3d,
                              Action<bool> onChanged, Func<string> label3d)
        {
            _stage = stage;
            _screen = stage.parent;
            _onChanged = onChanged;
            _label3d = label3d;

            var segment = Theme.Box("cg-segment");
            segment.Add(Theme.Label("View", "cg-chip__key"));
            _button2d = Segment("2D", false);
            _button3d = Segment("3D", true);
            segment.Add(_button2d);
            segment.Add(_button3d);
            var grow = bar.Q(className: "cg-grow");
            bar.Insert(grow != null ? bar.IndexOf(grow) : bar.childCount, segment);

            _label = Theme.Label("", "cg-stage__view3d");
            stage.Add(_label);
            var nav = Theme.Box("cg-stage__nav");
            nav.Add(Key("Right-drag", "Orbit"));
            nav.Add(Key("Middle-drag", "Pan (or Shift + right-drag)"));
            nav.Add(Key("Wheel", "Zoom"));
            nav.Add(Key("F", "Frame all"));
            nav.Add(Key("Home", "Worker's view"));
            stage.Add(nav);

            keys.Add(Key("V", "2D / 3D view"));
            Set(start3d);
        }

        Button Segment(string text, bool is3d)
        {
            var b = new Button(() => Set(is3d)) { text = text };
            b.AddToClassList("cg-segment__item");
            return b;
        }

        static VisualElement Key(string cap, string label)
        {
            var k = Theme.Box("cg-key");
            k.Add(Theme.Label(cap, "cg-key__cap"));
            k.Add(Theme.Label(label, "cg-key__label"));
            return k;
        }

        public void Poll()
        {
            if (VPressed()) Set(!_is3d);
            if (!_is3d || Time.unscaledTime < _nextLabel) return;
            _nextLabel = Time.unscaledTime + LabelRefreshS;
            _label.text = _label3d();
        }

        void Set(bool is3d)
        {
            _is3d = is3d;
            _screen?.EnableInClassList("cg-screen--solid", !is3d);
            _stage.EnableInClassList("cg-stage--3d", is3d);
            _button2d.EnableInClassList("cg-segment__item--on", !is3d);
            _button3d.EnableInClassList("cg-segment__item--on", is3d);
            _label.text = is3d ? _label3d() : "";
            _onChanged(is3d);
        }

        /// <summary>True where UI other than the stage is under this point (screen px, bottom-left origin).</summary>
        public bool OverPanel(Vector2 screenPx)
        {
            var panel = _stage.panel;
            if (panel == null) return false;
            var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPx.x, Screen.height - screenPx.y)));
            return picked != null && picked != _stage && !_stage.Contains(picked);
        }

#if ENABLE_INPUT_SYSTEM
        static bool VPressed() => Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
        static bool VPressed() => Input.GetKeyDown(KeyCode.V);
#endif
    }
}
