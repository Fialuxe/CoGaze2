using System;
using System.Collections.Generic;
using CoGaze.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// The expert PC's screen. Three states: a fatal configuration screen, the participant setup
    /// card, and the session view (top bar, checklist panel, video stage, key hints). Shows data; owns
    /// no experiment logic — actions go out through IExperimentControl and the setup callback.
    /// </summary>
    public sealed class ExpertScreenBehaviour : MonoBehaviour
    {
        const float RefreshIntervalS = 0.1f;

        UIDocument _doc;
        VisualElement _root;
        SessionBarView _bar;
        ChecklistView _checklist;
        VideoStageView _stage;
        VisualElement _keys, _rec, _overlay;
        ExperimenterKeys _input;
        ISessionStatus _session;
        float _nextRefresh;
        bool _overlayPinned;

        /// <summary>Called by the bootstrap before anything is shown.</summary>
        public void Build()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;   // 1 panel px = 1 screen px, so gaze px map directly
            settings.scale = 1f;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();

            gameObject.SetActive(false);
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            gameObject.SetActive(true);

            _root = _doc.rootVisualElement;
            _root.styleSheets.Add(Theme.StyleSheet());
            _root.style.unityFontDefinition = FontDefinition.FromFont(Theme.Font());
            _root.AddToClassList("cg-root");
        }

        public void ShowFatal(string title, IReadOnlyList<string> problems, string footer)
        {
            _root.Clear();
            _session = null;
            _root.Add(FatalView.Build(title, problems, footer));
        }

        public void ShowSetup(IEnumerable<string> participants, string configPath, bool rehearsal, Func<string, string> openSession)
        {
            _root.Clear();
            _session = null;
            _root.Add(SetupView.Build(participants, configPath, rehearsal, openSession));
        }

        public void ShowSession(ISessionStatus session, IConditionSource conditions, IExperimentControl control,
                                IHealthBoard health, IClock clock, Rect videoRectPx, bool rehearsal)
        {
            _root.Clear();
            _session = session;

            var screen = new VisualElement();
            screen.AddToClassList("cg-screen");
            screen.AddToClassList("cg-screen--solid");
            _root.Add(screen);

            _stage = new VideoStageView(videoRectPx, rehearsal);
            screen.Add(_stage.Root);

            _overlay = new VisualElement();
            _overlay.AddToClassList("cg-screen");
            _overlay.pickingMode = PickingMode.Ignore;
            screen.Add(_overlay);

            _bar = new SessionBarView(session, conditions, clock, rehearsal);
            _overlay.Add(_bar.Root);
            _checklist = new ChecklistView(health, session, control);
            _overlay.Add(_checklist.Root);
            _keys = KeyHintsView.Build();
            _overlay.Add(_keys);

            _rec = RecordingIndicator.Build();
            _rec.style.display = DisplayStyle.None;
            screen.Add(_rec);

            _input = new ExperimenterKeys(session, control, () => _overlayPinned = !_overlayPinned);
            Refresh();
        }

        /// <summary>Beam's licence requires a visible indicator whenever it tracks (EXP-F-052).</summary>
        public void SetRecordingIndicator(bool visible)
        {
            if (_rec != null) _rec.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Rehearsal only: where the worker would draw the cue (sigma as a circle on the video).</summary>
        public void ShowCuePreview(bool valid, float u, float v, float radiusPx) => _stage?.ShowCue(valid, u, v, radiusPx);

        void Update()
        {
            if (_session == null) return;
            _input.Poll();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshIntervalS;
            Refresh();
        }

        void Refresh()
        {
            // During a trial the participant sees only the video; the experimenter can pin the overlay with F1.
            bool showOverlay = _session.State != TrialState.InTrial || _overlayPinned;
            _overlay.style.display = showOverlay ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showOverlay) return;
            _bar.Refresh();
            _checklist.Refresh();
        }
    }
}
