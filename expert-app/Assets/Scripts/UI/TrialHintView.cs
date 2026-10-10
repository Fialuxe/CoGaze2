using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// A small, dim line during trials so the experimenter knows the state without the full panel.
    /// app.json "operator_hint_in_trial" turns it off when the expert must see nothing but video (TBD-08).
    /// </summary>
    sealed class TrialHintView
    {
        readonly ISessionStatus _session;
        readonly IClock _clock;
        readonly Label _text;

        public VisualElement Root { get; }

        public TrialHintView(ISessionStatus session, IClock clock)
        {
            _session = session;
            _clock = clock;
            Root = Theme.Box("cg-trialhint");
            Root.pickingMode = PickingMode.Ignore;
            Root.Add(Theme.Box("cg-trialhint__dot"));
            _text = Theme.Label("", "cg-trialhint__text");
            Root.Add(_text);
        }

        public void Refresh()
        {
            long s = (_clock.NowNs - _session.StateSinceNs) / 1_000_000_000L;
            _text.text = $"Trial {_session.TrialIndex + 1}/{_session.TrialCount} running  {s / 60:00}:{s % 60:00}    Space end · Shift+A abort · F1 panel";
        }
    }
}
