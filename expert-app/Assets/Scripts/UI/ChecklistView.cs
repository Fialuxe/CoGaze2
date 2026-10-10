using System.Collections.Generic;
using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// Pre-flight checklist (EXP-F-001, 002): one row per health item; red rows say what happened and
    /// what to do. The start gate itself lives in TrialRunner; NextStepView explains it.
    /// </summary>
    sealed class ChecklistView
    {
        readonly IHealthBoard _health;
        readonly VisualElement _rows;
        readonly Label _count;
        readonly List<ChecklistRow> _rowViews = new List<ChecklistRow>();

        public VisualElement Root { get; }

        public ChecklistView(IHealthBoard health)
        {
            _health = health;
            Root = Theme.Box("cg-card", "cg-section");
            var header = Theme.Box("cg-checklist__header");
            header.Add(Theme.Label("Checklist", "cg-section__title", "cg-grow"));
            _count = Theme.Label("", "cg-checklist__count");
            header.Add(_count);
            Root.Add(header);
            _rows = Theme.Box();
            Root.Add(_rows);
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
            _count.text = ok == items.Count ? "all ready" : $"{items.Count - ok} need attention";
            _count.EnableInClassList("cg-checklist__count--bad", ok != items.Count);
        }
    }
}
