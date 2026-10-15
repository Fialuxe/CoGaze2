using System;
using CoGaze.Expert.View3D.Model;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CoGaze.Expert.View3D
{
    /// <summary>
    /// Mouse and keys for the 3D view: right-drag orbits, middle-drag or Shift+right-drag pans, the
    /// wheel zooms, F frames everything, Home goes to the worker's view. A drag or wheel starts only
    /// over the stage and not over a panel on top of it; a drag, once started, continues outside.
    /// </summary>
    sealed class OrbitInput
    {
        enum Drag { None, Orbit, Pan }

        Drag _drag;
        Vector2 _last;

        /// <param name="viewport">The stage in bottom-left screen px.</param>
        /// <param name="overPanel">True where UI covers the stage (screen px, bottom-left).</param>
        public void Poll(OrbitCamera orbit, (float x, float y, float w, float h) viewport, float verticalFovDeg,
                         Func<Vector2, bool> overPanel, out bool frame, out bool home)
        {
            frame = KeyPressed(KeyName.F);
            home = KeyPressed(KeyName.Home);
            if (!HasMouse()) return;

            Vector2 pos = MousePosition();
            bool inside = StageViewport.Contains(viewport, pos.x, pos.y) && !overPanel(pos);

            if (_drag == Drag.None && inside)
            {
                if (ButtonPressed(1)) _drag = ShiftHeld() ? Drag.Pan : Drag.Orbit;
                else if (ButtonPressed(2)) _drag = Drag.Pan;
                _last = pos;
            }
            if ((_drag == Drag.Orbit && !ButtonHeld(1)) || (_drag == Drag.Pan && !ButtonHeld(1) && !ButtonHeld(2)))
                _drag = Drag.None;

            var delta = pos - _last;
            _last = pos;
            if (_drag == Drag.Orbit) orbit.Orbit(delta.x, delta.y);
            else if (_drag == Drag.Pan) orbit.Pan(delta.x, delta.y, viewport.h, verticalFovDeg);

            float scroll = ScrollY();
            if (inside && scroll != 0f)
                orbit.Zoom(Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll);   // raw Windows deltas are 120 a notch
        }

        enum KeyName { F, Home }

#if ENABLE_INPUT_SYSTEM
        static bool HasMouse() => Mouse.current != null;
        static Vector2 MousePosition() => Mouse.current.position.ReadValue();
        static float ScrollY() => Mouse.current.scroll.ReadValue().y;

        static bool ButtonPressed(int button) =>
            button == 1 ? Mouse.current.rightButton.wasPressedThisFrame : Mouse.current.middleButton.wasPressedThisFrame;

        static bool ButtonHeld(int button) =>
            button == 1 ? Mouse.current.rightButton.isPressed : Mouse.current.middleButton.isPressed;

        static bool ShiftHeld() => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        static bool KeyPressed(KeyName k)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return k == KeyName.F ? kb.fKey.wasPressedThisFrame : kb.homeKey.wasPressedThisFrame;
        }
#else
        static bool HasMouse() => Input.mousePresent;
        static Vector2 MousePosition() => Input.mousePosition;
        static float ScrollY() => Input.mouseScrollDelta.y;
        static bool ButtonPressed(int button) => Input.GetMouseButtonDown(button);
        static bool ButtonHeld(int button) => Input.GetMouseButton(button);
        static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        static bool KeyPressed(KeyName k) => Input.GetKeyDown(k == KeyName.F ? KeyCode.F : KeyCode.Home);
#endif
    }
}
