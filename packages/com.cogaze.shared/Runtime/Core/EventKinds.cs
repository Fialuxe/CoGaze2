namespace CoGaze.Core
{
    /// <summary>Values of the "kind" column of events.csv, so analysis can filter on them.</summary>
    public static class EventKinds
    {
        public const string SessionStart = "session_start";
        public const string SessionEnd = "session_end";
        public const string ReadyToStart = "ready_to_start";
        public const string ConditionSent = "condition_sent";
        public const string ConditionAck = "condition_ack";
        public const string ConditionMismatch = "condition_mismatch";
        public const string TrialStart = "trial_start";
        public const string TrialEnd = "trial_end";
        public const string TrialAbort = "trial_abort";
        public const string TrialFlagged = "trial_flagged";
        public const string SourceMismatch = "source_mismatch";
        public const string Health = "health";
        public const string WorkerEvent = "worker_event";
    }
}
