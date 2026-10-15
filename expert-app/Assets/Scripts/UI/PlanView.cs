using System.Collections.Generic;
using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>The participant's whole order: what has been done, what is running, what comes next.</summary>
    sealed class PlanView
    {
        readonly ISessionStatus _session;
        readonly List<(VisualElement row, VisualElement mark)> _rows = new List<(VisualElement, VisualElement)>();

        public VisualElement Root { get; }

        public PlanView(ISessionStatus session)
        {
            _session = session;
            Root = Theme.Box("cg-card", "cg-section");
            Root.Add(Theme.Label("Session plan", "cg-section__title"));
            foreach (var item in session.Plan)
            {
                var row = Theme.Box("cg-plan");
                var mark = Theme.Box("cg-plan__mark");
                row.Add(mark);
                row.Add(Theme.Label($"{item.Index + 1}", "cg-plan__num"));
                var text = Theme.Box("cg-plan__text");
                text.Add(Theme.Label($"{item.Condition.ConditionId} · {Describe.Kind(item.Kind)}", "cg-plan__title"));
                text.Add(Theme.Label(Describe.Condition(item.Condition), "cg-plan__desc"));
                row.Add(text);
                Root.Add(row);
                _rows.Add((row, mark));
            }
        }

        public void Refresh()
        {
            var plan = _session.Plan;
            for (int i = 0; i < plan.Count && i < _rows.Count; i++)
            {
                var (row, mark) = _rows[i];
                row.EnableInClassList("cg-plan--now", plan[i].State == PlanItemState.Current);
                row.EnableInClassList("cg-plan--done", plan[i].State == PlanItemState.Done);
                mark.EnableInClassList("cg-plan__mark--now", plan[i].State == PlanItemState.Current);
                mark.EnableInClassList("cg-plan__mark--done", plan[i].State == PlanItemState.Done);
            }
        }
    }
}
