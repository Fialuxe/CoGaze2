namespace CoGaze.Core
{
    /// <summary>Reliable control messages in both directions.</summary>
    public interface IMessageChannel
    {
        bool IsConnected { get; }
        void Send(in ControlMessage message);
        bool TryReceive(out ControlMessage message);
    }
}
