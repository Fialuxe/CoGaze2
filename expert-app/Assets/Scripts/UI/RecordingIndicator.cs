using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>The always-visible "eye tracking active" mark the Beam licence and EXP-F-052 require.</summary>
    static class RecordingIndicator
    {
        public static VisualElement Build()
        {
            var rec = Theme.Box("cg-rec");
            rec.pickingMode = PickingMode.Ignore;
            rec.Add(Theme.Box("cg-rec__dot"));
            rec.Add(Theme.Label("Eye tracking active", "cg-rec__label"));
            return rec;
        }
    }
}
