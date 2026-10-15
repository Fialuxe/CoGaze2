namespace CoGaze.Core
{
    /// <summary>
    /// A reliable message: exactly one payload field is meaningful, chosen by Kind. A tagged struct
    /// rather than a class hierarchy so it passes by `in` without allocating.
    /// </summary>
    public readonly struct ControlMessage
    {
        public readonly MessageKind Kind;
        public readonly Condition Condition;
        /// <summary>ConditionAck: the id the worker applied. Session: the session folder name.</summary>
        public readonly string Text;
        public readonly TrialMessage Trial;
        public readonly WorkerStatus Status;
        public readonly WorkerEvent Event;
        public readonly ClockPing Ping;

        ControlMessage(MessageKind kind, Condition condition = default, string text = null, TrialMessage trial = default,
                       WorkerStatus status = default, WorkerEvent evt = default, ClockPing ping = default)
        {
            Kind = kind;
            Condition = condition;
            Text = text;
            Trial = trial;
            Status = status;
            Event = evt;
            Ping = ping;
        }

        public static ControlMessage Of(in Condition c) => new ControlMessage(MessageKind.Condition, condition: c);
        public static ControlMessage Ack(string conditionId) => new ControlMessage(MessageKind.ConditionAck, text: conditionId);
        public static ControlMessage Of(in TrialMessage t) => new ControlMessage(MessageKind.Trial, trial: t);
        public static ControlMessage Of(in WorkerStatus s) => new ControlMessage(MessageKind.WorkerStatus, status: s);
        public static ControlMessage Of(in WorkerEvent e) => new ControlMessage(MessageKind.WorkerEvent, evt: e);
        public static ControlMessage Of(in ClockPing p) => new ControlMessage(MessageKind.ClockPing, ping: p);
        public static ControlMessage Session(string sessionName) => new ControlMessage(MessageKind.Session, text: sessionName);
    }
}
