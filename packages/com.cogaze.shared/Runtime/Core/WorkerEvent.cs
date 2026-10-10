namespace CoGaze.Core
{
    /// <summary>Something that happened on the worker side, e.g. a target selection (WRK-F-031).</summary>
    public readonly struct WorkerEvent
    {
        public readonly long TWorkerNs;
        public readonly string Kind;
        public readonly string Detail;

        public WorkerEvent(long tWorkerNs, string kind, string detail)
        {
            TWorkerNs = tWorkerNs;
            Kind = kind;
            Detail = detail;
        }
    }
}
