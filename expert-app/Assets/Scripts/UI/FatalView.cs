using System.Collections.Generic;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>Start-up refused: lists every problem found, never a default silently substituted.</summary>
    static class FatalView
    {
        public static VisualElement Build(string title, IReadOnlyList<string> problems, string footer)
        {
            var screen = Theme.Box("cg-screen", "cg-screen--solid", "cg-center");
            var card = Theme.Box("cg-card", "cg-fatal");
            screen.Add(card);
            card.Add(Theme.Label("CANNOT START", "cg-card__eyebrow"));
            card.Add(Theme.Label(title, "cg-card__title"));
            card.Add(Theme.Label($"{problems.Count} problem{(problems.Count == 1 ? "" : "s")} found.", "cg-card__subtitle"));

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.maxHeight = 520;
            foreach (var p in problems)
            {
                var item = Theme.Box("cg-fatal__item");
                item.Add(Theme.Box("cg-fatal__bullet"));
                item.Add(Theme.Label(p, "cg-fatal__text"));
                list.Add(item);
            }
            card.Add(list);
            card.Add(Theme.Box("cg-divider"));
            card.Add(Theme.Label(footer, "cg-muted"));
            return screen;
        }
    }
}
