namespace CoGaze.Core
{
    public static class LogSinkExtensions
    {
        /// <summary>One events.csv row: t_ns, kind, detail.</summary>
        public static void Event(this ILogSink sink, long tNs, string kind, string detail = "") =>
            sink.Write(LogSchema.Events, Csv.Row(tNs, kind, detail));
    }
}
