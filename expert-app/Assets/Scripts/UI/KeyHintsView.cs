using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>Keyboard shortcuts along the bottom edge (TBD-08: keys, so nothing pops up for the participant).</summary>
    static class KeyHintsView
    {
        public static VisualElement Build()
        {
            var keys = Theme.Box("cg-keys");
            keys.Add(Key("N", "Next condition"));
            keys.Add(Key("Space", "Start / end trial"));
            keys.Add(Key("Shift+A", "Abort trial"));
            keys.Add(Key("F1", "Show panel during trial"));
            return keys;
        }

        static VisualElement Key(string cap, string label)
        {
            var k = Theme.Box("cg-key");
            k.Add(Theme.Label(cap, "cg-key__cap"));
            k.Add(Theme.Label(label, "cg-key__label"));
            return k;
        }
    }
}
