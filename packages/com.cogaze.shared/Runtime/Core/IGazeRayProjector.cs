namespace CoGaze.Core
{
    /// <summary>Turns a screen gaze into a ray for the worker. 2D and 3D views each implement it.</summary>
    public interface IGazeRayProjector
    {
        bool TryProject(in GazeSample sample, out GazeRay ray);
    }
}
