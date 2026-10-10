using UnityEngine;
using UnityEngine.UIElements;

namespace CoGaze.Expert.UI
{
    /// <summary>Loads the shared CoGaze theme (com.cogaze.shared/Runtime/Theme) and the runtime font.</summary>
    static class Theme
    {
        public static StyleSheet StyleSheet()
        {
            var sheet = Resources.Load<StyleSheet>("CoGazeTheme");
            if (sheet == null) Debug.LogError("[UI] CoGazeTheme.uss not found; is com.cogaze.shared installed?");
            return sheet;
        }

        public static Font Font() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Label Label(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement Box(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }
    }
}
