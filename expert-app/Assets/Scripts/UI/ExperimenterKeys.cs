using System;
using CoGaze.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#else
using UnityEngine;
#endif

namespace CoGaze.Expert.UI
{
    /// <summary>Experimenter shortcuts (EXP-F-004, TBD-08). Abort needs Shift so it cannot happen by accident.</summary>
    sealed class ExperimenterKeys
    {
        readonly ISessionStatus _session;
        readonly IExperimentControl _control;
        readonly Action _toggleOverlay;

        public ExperimenterKeys(ISessionStatus session, IExperimentControl control, Action toggleOverlay)
        {
            _session = session;
            _control = control;
            _toggleOverlay = toggleOverlay;
        }

        public void Poll()
        {
            if (Pressed(KeyName.N)) _control.PrepareNext();
            if (Pressed(KeyName.Space))
            {
                if (_session.State == TrialState.InTrial) _control.EndTrial();
                else _control.StartTrial();
            }
            if (Pressed(KeyName.A) && ShiftHeld()) _control.AbortTrial();
            if (Pressed(KeyName.F1)) _toggleOverlay();
        }

        enum KeyName { N, Space, A, F1 }

#if ENABLE_INPUT_SYSTEM
        static bool Pressed(KeyName k)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case KeyName.N: return kb.nKey.wasPressedThisFrame;
                case KeyName.Space: return kb.spaceKey.wasPressedThisFrame;
                case KeyName.A: return kb.aKey.wasPressedThisFrame;
                default: return kb.f1Key.wasPressedThisFrame;
            }
        }

        static bool ShiftHeld() => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
#else
        static bool Pressed(KeyName k)
        {
            switch (k)
            {
                case KeyName.N: return Input.GetKeyDown(KeyCode.N);
                case KeyName.Space: return Input.GetKeyDown(KeyCode.Space);
                case KeyName.A: return Input.GetKeyDown(KeyCode.A);
                default: return Input.GetKeyDown(KeyCode.F1);
            }
        }

        static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }
}
