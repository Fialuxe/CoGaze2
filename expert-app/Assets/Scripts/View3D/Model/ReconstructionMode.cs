namespace CoGaze.Expert.View3D.Model
{
    public enum ReconstructionMode
    {
        NotConfigured,
        /// <summary>Chunk updates from server/service.py over a WebSocket.</summary>
        Live,
        /// <summary>.cgzm files replayed from disk.</summary>
        Replay,
    }
}
