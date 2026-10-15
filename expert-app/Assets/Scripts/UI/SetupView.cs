using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>
    /// Opens a session for a participant. The id must have an order in conditions.json; the callback
    /// returns an error message to show, or null on success.
    /// </summary>
    static class SetupView
    {
        public static VisualElement Build(IEnumerable<string> participants, string configPath, bool rehearsal,
                                          Func<string, string> openSession)
        {
            var screen = Theme.Box("cg-screen", "cg-screen--solid", "cg-center");
            var card = Theme.Box("cg-card", "cg-setup");
            screen.Add(card);

            card.Add(Theme.Label(rehearsal ? "REHEARSAL" : "NEW SESSION", "cg-card__eyebrow"));
            card.Add(Theme.Label("Start a session", "cg-card__title"));
            card.Add(Theme.Label("Enter the participant ID. A session that crashed resumes after its last finished trial.",
                                 "cg-card__subtitle"));

            var field = new TextField { value = "" };
            field.AddToClassList("cg-field");
            card.Add(field);

            var choices = Theme.Box("cg-choices");
            foreach (var p in participants)
            {
                string id = p;
                var chip = new Button(() => field.value = id) { text = id };
                chip.AddToClassList("cg-choice");
                choices.Add(chip);
            }
            card.Add(choices);

            var error = Theme.Label("", "cg-error-text");
            error.style.display = DisplayStyle.None;
            card.Add(error);

            var open = new Button(() =>
            {
                string message = openSession(field.value.Trim());
                error.text = message ?? "";
                error.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
            }) { text = "Open session" };
            open.AddToClassList("cg-button");
            card.Add(open);

            card.Add(Theme.Box("cg-divider"));
            card.Add(Theme.Label($"Config · {configPath}", "cg-faint"));
            field.Focus();
            return screen;
        }
    }
}
