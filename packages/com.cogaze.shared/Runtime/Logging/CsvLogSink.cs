using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CoGaze.Core;

namespace CoGaze.Logging
{
    /// <summary>
    /// Writes CSV tables from a background thread. The caller only enqueues, so a slow disk never
    /// stalls rendering. Flushes every second and on <see cref="Flush"/> (trial end), so a crash loses
    /// at most the last second (COM-F-011). Files are appended, never overwritten.
    /// </summary>
    public sealed class CsvLogSink : ILogSink, IDisposable
    {
        public const int FlushIntervalMs = 1000;

        readonly string _directory;
        readonly ConcurrentQueue<(string table, string row)> _queue = new ConcurrentQueue<(string, string)>();
        readonly Dictionary<string, StreamWriter> _writers = new Dictionary<string, StreamWriter>();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly Thread _thread;
        int _depth;
        volatile bool _flushRequested;
        volatile bool _stopping;
        volatile string _error;

        public CsvLogSink(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
            _thread = new Thread(Run) { IsBackground = true, Name = "CsvLogSink" };
            _thread.Start();
        }

        /// <summary>Rows waiting to be written. Shown on the checklist; red above a threshold.</summary>
        public int QueueDepth => Volatile.Read(ref _depth);

        /// <summary>Last I/O error from the writer thread, null when healthy.</summary>
        public string Error => _error;

        public void Write(string table, string row)
        {
            if (LogSchema.ColumnsOf(table) == null)
                throw new ArgumentException($"Unknown log table '{table}'. Add it to LogSchema.", nameof(table));
            _queue.Enqueue((table, row));
            Interlocked.Increment(ref _depth);
        }

        /// <summary>Write everything queued so far and flush to disk soon (asynchronously).</summary>
        public void Flush()
        {
            _flushRequested = true;
            _wake.Set();
        }

        /// <summary>Blocks until the queue is written and flushed. For shutdown and tests.</summary>
        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            _thread.Join();
            _wake.Dispose();
        }

        void Run()
        {
            var lastFlush = DateTime.UtcNow;
            while (true)
            {
                _wake.WaitOne(50);
                Drain();
                bool due = (DateTime.UtcNow - lastFlush).TotalMilliseconds >= FlushIntervalMs;
                if (due || _flushRequested || _stopping)
                {
                    _flushRequested = false;
                    foreach (var w in _writers.Values) Safe(() => w.Flush());
                    lastFlush = DateTime.UtcNow;
                }
                if (_stopping && _queue.IsEmpty) break;
            }
            foreach (var w in _writers.Values) Safe(w.Dispose);
            _writers.Clear();
        }

        void Drain()
        {
            while (_queue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _depth);
                var w = WriterFor(item.table);
                if (w != null) Safe(() => w.WriteLine(item.row));
            }
        }

        StreamWriter WriterFor(string table)
        {
            if (_writers.TryGetValue(table, out var w)) return w;
            string path = Path.Combine(_directory, table + ".csv");
            bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            try
            {
                w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                                     new UTF8Encoding(false));
                if (isNew) w.WriteLine(string.Join(",", LogSchema.ColumnsOf(table)));
            }
            catch (Exception e)
            {
                _error = $"{table}.csv: {e.Message}";
                return null;
            }
            _writers[table] = w;
            return w;
        }

        void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { _error = e.Message; }
        }
    }
}
