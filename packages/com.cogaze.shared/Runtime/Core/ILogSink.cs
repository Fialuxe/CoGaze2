namespace CoGaze.Core
{
    /// <summary>
    /// Appends one CSV row to a table. Rows are queued and written off the main thread. The first
    /// column of every table is t_ns from IClock; callers include it.
    /// </summary>
    public interface ILogSink
    {
        void Write(string table, string row);
    }
}
