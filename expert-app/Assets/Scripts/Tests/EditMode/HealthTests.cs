using System.Collections.Generic;
using System.Linq;
using CoGaze.Core;
using CoGaze.Expert.Experiment;
using CoGaze.Expert.Pipeline;
using CoGaze.Expert.View2D;
using CoGaze.Logging;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    public class HealthTests
    {
        static HealthItem Item(HealthBoard b, string key)
        {
            Assert.IsTrue(b.TryGet(key, out var item), key);
            return item;
        }

        [Test]
        public void WorkerSilence_TurnsLinkRed_StatusTurnsItGreen()
        {
            var clock = new TestClock { NowNs = 1_000_000_000 };
            var board = new HealthBoard(new MemoryLog(), clock);
            var monitor = new WorkerStatusMonitor(board, clock, 3f);

            monitor.Tick();
            Assert.AreEqual(HealthLevel.Error, Item(board, "worker_link").Level);

            monitor.Handle(ControlMessage.Of(new WorkerStatus(72f, AlignmentState.Aligned, true, 0)));
            monitor.Tick();
            Assert.AreEqual(HealthLevel.Ok, Item(board, "worker_link").Level);
            Assert.AreEqual(HealthLevel.Ok, Item(board, "worker_alignment").Level);

            clock.AdvanceMs(3100);
            monitor.Tick();
            Assert.AreEqual(HealthLevel.Error, Item(board, "worker_link").Level);
            Assert.IsFalse(board.AllOk);
        }

        [Test]
        public void LostAlignment_IsRedWithAnAction()
        {
            var clock = new TestClock();
            var board = new HealthBoard(null, clock);
            var monitor = new WorkerStatusMonitor(board, clock, 3f);
            monitor.Handle(ControlMessage.Of(new WorkerStatus(72f, AlignmentState.TrackingLost, true, 0)));
            monitor.Tick();
            var a = Item(board, "worker_alignment");
            Assert.AreEqual(HealthLevel.Error, a.Level);
            Assert.IsNotEmpty(a.Problem);
            Assert.IsNotEmpty(a.Action);
        }

        [Test]
        public void SourceThatStops_GoesRedAfterTimeout()
        {
            var clock = new TestClock { NowNs = 1_000_000_000 };
            var board = new HealthBoard(new MemoryLog(), clock);
            var cond = new FixedCondition { Current = Conditions.Make("C", "beam") };
            var beam = new ScriptedSource("beam");
            var pipeline = new GazePipeline(new IGazeSource[] { beam }, cond,
                new View2DProjector(new VideoRect(0, 0, 100, 100), new DisplayedFrameLog()),
                new FixedSigmaPolicy(cond), new RecordingSender(), new MemoryLog(), clock,
                new Dictionary<string, FilterSettings> { ["default"] = FilterSettings.PassThrough }, 30f);
            var reporter = new SourceHealthReporter(pipeline, board, clock, 3f,
                new Dictionary<string, string> { ["beam"] = "Check Beam." });

            Assert.AreEqual(HealthLevel.Unknown, Item(board, "source_beam").Level, "waiting before any sample");
            for (int i = 0; i < 60; i++)
            {
                clock.AdvanceMs(33);
                beam.Push(clock.NowNs, 50, 50);
                pipeline.Tick();
                reporter.Tick();
            }
            Assert.AreEqual(HealthLevel.Ok, Item(board, "source_beam").Level);

            for (int i = 0; i < 5; i++)
            {
                clock.AdvanceMs(1000);
                pipeline.Tick();
                reporter.Tick();
            }
            var item = Item(board, "source_beam");
            Assert.AreEqual(HealthLevel.Error, item.Level);
            Assert.AreEqual("Check Beam.", item.Action);
        }

        [Test]
        public void EmptyChecklist_NeverAllowsStart()
        {
            var board = new HealthBoard(null, new TestClock());
            Assert.IsFalse(board.AllOk);
            Assert.IsFalse(board.Items.Any());
        }
    }
}
