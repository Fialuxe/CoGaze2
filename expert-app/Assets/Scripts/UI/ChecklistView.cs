using System.Collections.Generic;
using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// Pre-flight checklist (EXP-F-001, 002): one row per health item, red rows explaining what
    /// happened and what to do. The primary button starts the trial and stays disabled — with the
    /// reason under it — until everything is green and the worker agrees on the condition.
    /// </summary>
    sealed class ChecklistView
    {
        readonly IHealthBoard _health;
        readonly ISessionStatus _session;
        readonly IExperimentControl _control;
        readonly VisualElement _rows;
        readonly Label _count, _hint;
        readonly Button _primary;
        readonly List<ChecklistRow> _rowViews = new List<ChecklistRow>();

        public VisualElement Root { get; }

        public ChecklistView(IHealthBoard health, ISessionStatus session, IExperimentControl control)
        {
            _health = health;
            _session = session;
            _control = control;

            Root = Theme.Box("cg-card", "cg-panel");
            var header = Theme.Box("cg-checklist__header");
            header.Add(Theme.Label("Checklist", "cg-checklist__title"));
            _count = Theme.Label("", "cg-checklist__count");
            header.Add(_count);
            Root.Add(header);

            _rows = new ScrollView(ScrollViewMode.Vertical);
            _rows.style.flexShrink = 1;
            Root.Add(_rows);

            Root.Add(Theme.Box("cg-divider"));
            _primary = new Button(OnPrimary);
            _primary.AddToClassList("cg-button");
            Root.Add(_primary);
            _hint = Theme.Label("", "cg-hint");
            Root.Add(_hint);
        }

        void OnPrimary()
        {
            if (_session.State == TrialState.Idle || _session.State == TrialState.InterTrial) _control.PrepareNext();
            else if (_session.State == TrialState.InTrial) _control.EndTrial();
            else _control.StartTrial();
        }

        public void Refresh()
        {
            var items = _health.Items;
            while (_rowViews.Count < items.Count)
            {
                var row = new ChecklistRow();
                _rowViews.Add(row);
                _rows.Add(row.Root);
            }
            int ok = 0;
            for (int i = 0; i < items.Count; i++)
            {
                _rowViews[i].Bind(items[i]);
                if (items[i].Level == HealthLevel.Ok) ok++;
            }
            _count.text = $"{ok} / {items.Count} ready";
            RefreshPrimary();
        }

        void RefreshPrimary()
        {
            switch (_session.State)
            {
                case TrialState.Idle:
                case TrialState.InterTrial:
                    bool more = _session.NextConditionId != null;
                    _primary.text = more ? $"Send condition {_session.NextConditionId}" : "Finish session";
                    _primary.SetEnabled(true);
                    _hint.text = "N  sends the next condition to the worker";
                    break;
                case TrialState.Ready:
                    _primary.text = "Start trial";
                    _primary.SetEnabled(_session.CanStartTrial);
                    _hint.text = _session.AwaitingWorker ? "Waiting for the worker to confirm the condition…"
                        : !_health.AllOk ? "Resolve the highlighted items to start"
                        : "Space  starts the trial";
                    break;
                case TrialState.InTrial:
                    _primary.text = "End trial";
                    _primary.SetEnabled(true);
                    _hint.text = "Space  ends the trial  ·  Shift+A  aborts it";
                    break;
                default:
                    _primary.text = "Session finished";
                    _primary.SetEnabled(false);
                    _hint.text = "All trials are done. Data is in the session folder.";
                    break;
            }
        }
    }
}
