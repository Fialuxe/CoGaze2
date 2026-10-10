using System;
using System.IO;
using System.Linq;
using CoGaze.Core;
using CoGaze.Logging;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class LoggingTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cogaze-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Sink_WritesHeaderFromSchemaThenRows()
        {
            using (var sink = new CsvLogSink(_dir))
            {
                sink.Event(10, EventKinds.TrialStart, "1");
                sink.Event(20, EventKinds.TrialEnd, "1");
            }
            var lines = File.ReadAllLines(Path.Combine(_dir, "events.csv"));
            Assert.AreEqual(string.Join(",", LogSchema.ColumnsOf(LogSchema.Events)), lines[0]);
            Assert.AreEqual("10,trial_start,1", lines[1]);
            Assert.AreEqual(3, lines.Length);
        }

        [Test]
        public void Sink_AppendsWithoutRepeatingHeader()
        {
            using (var sink = new CsvLogSink(_dir)) sink.Event(1, "a");
            using (var sink = new CsvLogSink(_dir)) sink.Event(2, "b");
            var lines = File.ReadAllLines(Path.Combine(_dir, "events.csv"));
            Assert.AreEqual(3, lines.Length);
        }

        [Test]
        public void Sink_RejectsUnknownTable()
        {
            using (var sink = new CsvLogSink(_dir))
                Assert.Throws<ArgumentException>(() => sink.Write("nope", "1"));
        }

        [Test]
        public void Sink_QueueDrainsToZero()
        {
            using (var sink = new CsvLogSink(_dir))
            {
                for (int i = 0; i < 1000; i++) sink.Event(i, "x");
                sink.Flush();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (sink.QueueDepth > 0 && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(10);
                Assert.AreEqual(0, sink.QueueDepth);
            }
        }

        [Test]
        public void SessionFolder_RefusesExistingFolder()
        {
            var start = new DateTime(2026, 10, 10, 9, 30, 0);
            var manifest = new SessionManifest { AppName = "test", AppConfigJson = "{\"a\":1}" };
            string path = SessionFolder.Create(_dir, "07", start, manifest);
            StringAssert.EndsWith("P07_20261010-093000", path);
            Assert.IsTrue(File.Exists(Path.Combine(path, "session.json")));
            StringAssert.Contains("\"log_format_version\": 1", File.ReadAllText(Path.Combine(path, "session.json")));
            Assert.Throws<IOException>(() => SessionFolder.Create(_dir, "07", start, manifest));
        }

        [Test]
        public void HealthBoard_LogsOnlyLevelChanges()
        {
            using (var sink = new CsvLogSink(_dir))
            {
                var board = new HealthBoard(sink, new FakeClock());
                board.Set(new HealthItem("beam", "Beam", HealthLevel.Ok, "30 Hz"));
                board.Set(new HealthItem("beam", "Beam", HealthLevel.Ok, "29 Hz"));
                board.Set(new HealthItem("beam", "Beam", HealthLevel.Error, "", "No data.", "Check Beam."));
                Assert.IsFalse(board.AllOk);
                Assert.AreEqual(1, board.Items.Count);
            }
            var rows = File.ReadAllLines(Path.Combine(_dir, "events.csv")).Skip(1).ToArray();
            Assert.AreEqual(2, rows.Length);
            StringAssert.Contains("beam=error", rows[1]);
        }

        [Test]
        public void HealthBoard_EmptyIsNotOk()
        {
            Assert.IsFalse(new HealthBoard(null, new FakeClock()).AllOk);
        }
    }
}
