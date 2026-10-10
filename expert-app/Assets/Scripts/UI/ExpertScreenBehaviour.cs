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
        NextStepView _next;
        PlanView _plan;
        TrialHintView _hint;
        bool _hintInTrial;
        VideoStageView _stage;
        VisualElement _keys, _rec, _overlay;
        ExperimenterKeys _input;
        ISessionStatus _session;
        float _nextRefresh;
        bool _overlayPinned;
        ViewSwitchView _viewSwitch;

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
                                IHealthBoard health, IClock clock, Rect videoRectPx, bool rehearsal, bool hintInTrial)
        {
            _root.Clear();
            _session = session;
            _hintInTrial = hintInTrial;

            var screen = Theme.Box("cg-screen", "cg-screen--solid");
            _root.Add(screen);

            _stage = new VideoStageView(videoRectPx, rehearsal, conditions);
            screen.Add(_stage.Root);

            _overlay = Theme.Box("cg-screen");
            _overlay.pickingMode = PickingMode.Ignore;
            screen.Add(_overlay);

            _bar = new SessionBarView(session, conditions, clock, rehearsal);
            _overlay.Add(_bar.Root);

            // Right column, top to bottom: what to do next, what is wrong, where we are in the plan.
            var column = new ScrollView(ScrollViewMode.Vertical);
            column.AddToClassList("cg-panel");
            _next = new NextStepView(session, health, control);
            column.Add(_next.Root);
            _checklist = new ChecklistView(health);
            column.Add(_checklist.Root);
            _plan = new PlanView(session);
            column.Add(_plan.Root);
            _overlay.Add(column);

            _keys = KeyHintsView.Build();
            _overlay.Add(_keys);

            _hint = new TrialHintView(session, clock);
            screen.Add(_hint.Root);

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

        /// <summary>Rehearsal only: what the worker would see — the condition's shape at the cue's position and size.</summary>
        public void ShowCuePreview(bool valid, float u, float v, float radiusPx) => _stage?.ShowCue(valid, u, v, radiusPx);

        /// <summary>Live: the worker video in the stage (#22); null texture shows the placeholder.</summary>
        public void ShowVideo(Texture texture, Rect sourcePx, Rect imagePx) => _stage?.ShowVideo(texture, sourcePx, imagePx);
        /// <summary>Adds the 2D / 3D switch (top bar and V) after ShowSession; the 3D view itself lives in View3D.</summary>
        public void ShowViewSwitch(bool start3d, Action<bool> onChanged, Func<string> label3d) =>
            _viewSwitch = new ViewSwitchView(_stage.Root, _bar.Root, _keys, start3d, onChanged, label3d);

        /// <summary>True where UI other than the stage covers this point (screen px, bottom-left), so 3D drags leave it alone.</summary>
        public bool IsPointerOverPanel(Vector2 screenPx) => _viewSwitch != null && _viewSwitch.OverPanel(screenPx);

        void Update()
        {
            if (_session == null) return;
            _input.Poll();
            _viewSwitch?.Poll();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshIntervalS;
            Refresh();
        }

        void Refresh()
        {
            // During a trial the participant sees only the video; the experimenter can pin the overlay with F1.
            bool inTrial = _session.State == TrialState.InTrial;
            bool showOverlay = !inTrial || _overlayPinned;
            _overlay.style.display = showOverlay ? DisplayStyle.Flex : DisplayStyle.None;
            _hint.Root.style.display = inTrial && !showOverlay && _hintInTrial ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showOverlay)
            {
                _hint.Refresh();
                return;
            }
            _bar.Refresh();
            _next.Refresh();
            _checklist.Refresh();
            _plan.Refresh();
        }
    }
}
