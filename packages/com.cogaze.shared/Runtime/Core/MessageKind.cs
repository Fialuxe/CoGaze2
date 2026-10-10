namespace CoGaze.Core
{
    /// <summary>Reliable control messages between expert and worker. Values are on the wire; never renumber.</summary>
    public enum MessageKind : byte
    {
        Condition = 1,
        ConditionAck = 2,
        Trial = 3,
        WorkerStatus = 4,
        WorkerEvent = 5,
        ClockPing = 6,
        Session = 7,
    }
}
