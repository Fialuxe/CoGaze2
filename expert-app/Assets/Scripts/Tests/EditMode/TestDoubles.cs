using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Expert.Tests
{
    public sealed class TestClock : IClock
    {
        public long NowNs { get; set; }
        public void AdvanceMs(double ms) => NowNs += (long)(ms * 1_000_000);
    }

    /// <summary>Keeps rows in memory, per table.</summary>
    public sealed class MemoryLog : ILogSink
    {
        public readonly Dictionary<string, List<string>> Rows = new Dictionary<string, List<string>>();

        public void Write(string table, string row)
        {
            if (!Rows.TryGetValue(table, out var list)) Rows[table] = list = new List<string>();
            list.Add(row);
        }

        public List<string> Of(string table) => Rows.TryGetValue(table, out var l) ? l : new List<string>();
    }

    public sealed class RecordingSender : ICueSender
    {
        public readonly List<GazeCue> Sent = new List<GazeCue>();
        public void Send(in GazeCue cue) => Sent.Add(cue);
    }

    public sealed class FixedCondition : IConditionSource
    {
        public bool HasCondition { get; set; } = true;
        public Condition Current { get; set; }
        public bool InTrial { get; set; } = true;
        public int TrialIndex { get; set; }
    }

    public sealed class ScriptedSource : IGazeSource
    {
        readonly Queue<GazeSample> _q = new Queue<GazeSample>();
        public ScriptedSource(string name) { Name = name; }
        public string Name { get; }
        public void Push(long tNs, float x, float y, bool valid = true, int confidence = -1) =>
            _q.Enqueue(new GazeSample(Name, tNs, tNs, valid, x, y, GazeMode.Eye, float.NaN, confidence));
        public bool TryRead(out GazeSample s)
        {
            if (_q.Count > 0) { s = _q.Dequeue(); return true; }
            s = default;
            return false;
        }
    }

    public sealed class StubCalibration : ICalibrationState
    {
        public bool HasResult { get; set; }
        public float MeanValidationErrorDeg { get; set; }
    }

    public static class Conditions
    {
        public static Condition Make(string id, string source, SigmaSource sigma = SigmaSource.Fixed, float fixedSigma = 2f,
                                     bool show = true, string filter = "default") =>
            new Condition(id, source, Visualization.Circle, show, sigma, fixedSigma, filter, "identify_A");
    }
}
