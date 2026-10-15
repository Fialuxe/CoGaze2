using CoGaze.Core;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>One checklist row; updated in place so the list does not flicker.</summary>
    sealed class ChecklistRow
    {
        readonly VisualElement _dot;
        readonly Label _label, _value, _problem, _action;
        HealthLevel _level = (HealthLevel)(-1);

        public VisualElement Root { get; }

        public ChecklistRow()
        {
            Root = Theme.Box("cg-check");
            var main = Theme.Box("cg-check__main");
            _dot = Theme.Box("cg-dot");
            main.Add(_dot);
            _label = Theme.Label("", "cg-check__label");
            main.Add(_label);
            _value = Theme.Label("", "cg-check__value");
            main.Add(_value);
            Root.Add(main);
            _problem = Theme.Label("", "cg-check__problem");
            Root.Add(_problem);
            _action = Theme.Label("", "cg-check__action");
            Root.Add(_action);
        }

        public void Bind(in HealthItem item)
        {
            _label.text = item.Label;
            _value.text = item.Value;
            bool bad = item.Level == HealthLevel.Error || item.Level == HealthLevel.Warning;
            _problem.text = item.Problem;
            _action.text = item.Action.Length > 0 ? "→  " + item.Action : "";
            _problem.style.display = bad && item.Problem.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _action.style.display = bad && item.Action.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (item.Level == _level) return;
            _level = item.Level;
            Root.RemoveFromClassList("cg-check--error");
            Root.RemoveFromClassList("cg-check--warning");
            _dot.RemoveFromClassList("cg-dot--ok");
            _dot.RemoveFromClassList("cg-dot--warning");
            _dot.RemoveFromClassList("cg-dot--error");
            switch (item.Level)
            {
                case HealthLevel.Ok: _dot.AddToClassList("cg-dot--ok"); break;
                case HealthLevel.Warning: _dot.AddToClassList("cg-dot--warning"); Root.AddToClassList("cg-check--warning"); break;
                case HealthLevel.Error: _dot.AddToClassList("cg-dot--error"); Root.AddToClassList("cg-check--error"); break;
            }
        }
    }
}
