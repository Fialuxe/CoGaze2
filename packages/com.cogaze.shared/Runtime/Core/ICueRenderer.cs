namespace CoGaze.Core
{
    /// <summary>One way of drawing gaze: Ray, Circle or Frustum (WRK-F-022). A new style is a new implementation.</summary>
    public interface ICueRenderer
    {
        Visualization Kind { get; }
        void Show(in CueVisual visual);
        void Hide();
    }
}
