using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoGaze.Core;
using CoGaze.Expert.Experiment;
using CoGaze.Logging;
using CoGaze.Transport.Loopback;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    public class ExperimentTests
    {
        static readonly string[] Filters = { "default", "none" };

        const string Good = @"{
          ""conditions"": [
            { ""condition_id"": ""A"", ""display_source"": ""beam"", ""visualization"": ""circle"", ""show_uncertainty"": true, ""sigma_source"": ""fixed"", ""fixed_sigma"": 3, ""filter"": ""default"", ""task"": ""t"" },
            { ""condition_id"": ""B"", ""display_source"": ""none"", ""visualization"": ""ray"", ""show_uncertainty"": false, ""sigma_source"": ""calib"", ""filter"": ""none"", ""task"": ""t"" }
          ],
          ""orders"": { ""P01"": [ { ""condition_id"": ""A"", ""kind"": ""practice"" }, { ""condition_id"": ""B"" }, { ""condition_id"": ""A"" } ] }
        }";

        [Test]
        public void GoodFile_Parses()
        {
            var errors = new List<string>();
            var set = ConditionSet.Parse(Good, Filters, errors);
            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(2, set.Conditions.Count);
            Assert.AreEqual(TrialKind.Practice, set.Orders["P01"][0].Kind);
            Assert.AreEqual(TrialKind.Main, set.Orders["P01"][1].Kind);
            Assert.IsNaN(set.Conditions["B"].FixedSigmaDeg);
        }

        [Test]
        public void BrokenFile_ReportsEveryProblem()
        {
            const string bad = @"{
              ""conditions"": [
                { ""condition_id"": ""A"", ""display_source"": ""kinect"", ""visualization"": ""circle"", ""show_uncertainty"": true, ""sigma_source"": ""fixed"", ""filter"": ""default"", ""task"": ""t"" },
                { ""condition_id"": ""A"", ""display_source"": ""beam"", ""visualization"": ""circle"", ""show_uncertainty"": true, ""sigma_source"": ""calib"", ""filter"": ""default"", ""task"": ""t"" },
                { ""condition_id"": ""C"", ""display_source"": ""beam"", ""visualization"": ""cone"", ""show_uncertainty"": ""yes"", ""sigma_source"": ""calib"", ""filter"": ""smooth"", ""task"": ""t"" }
              ],
              ""orders"": { ""P01"": [ { ""condition_id"": ""Z"" } ] }
            }";
            var errors = new List<string>();
            Assert.IsNull(ConditionSet.Parse(bad, Filters, errors));
            string all = string.Join("\n", errors);
            StringAssert.Contains("unknown display_source \"kinect\"", all);
            StringAssert.Contains("fixed_sigma is missing", all);
            StringAssert.Contains("duplicate condition_id", all);
            StringAssert.Contains("visualization must be", all);
            StringAssert.Contains("show_uncertainty must be", all);
            StringAssert.Contains("filter \"smooth\"", all);
            StringAssert.Contains("unknown condition_id \"Z\"", all);
        }

        [Test]
        public void RepositorySample_IsValid()
        {
            string path = Path.Combine(RepoRoot(), "config", "conditions.json");
            var errors = new List<string>();
            ConditionSet.Parse(File.ReadAllText(path), Filters, errors);
            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
        }

        static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        {
            var d = new DirectoryInfo(Path.GetDirectoryName(here));
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "config"))) d = d.Parent;
            return d.FullName;
        }

        sealed class Rig
        {
            public readonly TestClock Clock = new TestClock();
            public readonly MemoryLog Log = new MemoryLog();
            public readonly LoopbackTransport Worker;
            public readonly HealthBoard Health;
            public readonly TrialRunner Runner;

            public Rig(string sessionDir = null, int start = 0, bool mismatch = false)
            {
                var set = ConditionSet.Parse(Good, Filters, new List<string>());
                Worker = new LoopbackTransport(Clock) { AckMismatch = mismatch };
                Health = new HealthBoard(Log, Clock);
                Runner = new TrialRunner("P01", set.Orders["P01"], set.Conditions, Worker, Log, Clock, Health, sessionDir, start);
            }

            public void Pump()
            {
                Clock.AdvanceMs(10);
                while (Worker.TryReceive(out var m)) Runner.Handle(m);
                Runner.Tick();
            }
        }

        [Test]
        public void Trial_StartsOnlyAfterWorkerEchoesCondition()
        {
            var r = new Rig();
            r.Runner.PrepareNext();
            Assert.IsTrue(r.Runner.AwaitingWorker);
            Assert.IsFalse(r.Runner.HasCondition, "no gaze before the worker agrees");
            r.Runner.StartTrial();
            Assert.AreEqual(TrialState.Ready, r.Runner.State);

            r.Pump();
            Assert.IsTrue(r.Runner.HasCondition);
            Assert.AreEqual("A", r.Runner.Current.ConditionId);
            r.Runner.StartTrial();
            Assert.AreEqual(TrialState.InTrial, r.Runner.State);
        }

        [Test]
        public void MismatchedEcho_BlocksStartAndTurnsChecklistRed()
        {
            var r = new Rig(mismatch: true);
            r.Runner.PrepareNext();
            r.Pump();
            r.Runner.StartTrial();
            Assert.AreNotEqual(TrialState.InTrial, r.Runner.State);
            Assert.IsFalse(r.Runner.HasCondition);
            Assert.IsFalse(r.Health.AllOk);
            Assert.IsTrue(r.Log.Of(LogSchema.Events).Any(x => x.Contains(EventKinds.ConditionMismatch)));
        }

        [Test]
        public void Condition_CannotChangeDuringTrial()
        {
            var r = new Rig();
            r.Runner.PrepareNext();
            r.Pump();
            r.Runner.StartTrial();
            r.Runner.PrepareNext();
            r.Pump();
            Assert.AreEqual(TrialState.InTrial, r.Runner.State);
            Assert.AreEqual("A", r.Runner.Current.ConditionId);

            r.Runner.EndTrial();
            r.Runner.PrepareNext();
            r.Pump();
            Assert.AreEqual("B", r.Runner.Current.ConditionId);
        }

        [Test]
        public void Abort_RepeatsTheSameEntry()
        {
            var r = new Rig();
            r.Runner.PrepareNext();
            r.Pump();
            r.Runner.StartTrial();
            r.Runner.AbortTrial();
            r.Runner.PrepareNext();
            r.Pump();
            Assert.AreEqual(0, r.Runner.TrialIndex);
            Assert.IsTrue(r.Log.Of(LogSchema.Events).Any(x => x.Contains(EventKinds.TrialAbort)));
        }

        [Test]
        public void RedChecklistDuringTrial_FlagsIt()
        {
            var r = new Rig();
            r.Runner.PrepareNext();
            r.Pump();
            r.Runner.StartTrial();
            r.Health.Set(new HealthItem("beam", "Beam", HealthLevel.Error, "", "No data.", "Check Beam."));
            r.Pump();
            r.Runner.EndTrial();
            Assert.IsTrue(r.Log.Of(LogSchema.Events).Any(x => x.Contains(EventKinds.TrialFlagged)));
            Assert.IsTrue(r.Log.Of(LogSchema.Events).Any(x => x.Contains(EventKinds.TrialEnd) && x.Contains("flagged=1")));
        }

        [Test]
        public void Progress_ResumesAfterLastFinishedTrial()
        {
            string root = Path.Combine(Path.GetTempPath(), "cogaze-tests", Guid.NewGuid().ToString("N"));
            string first = Path.Combine(root, "P01_20261010-090000");
            Directory.CreateDirectory(first);
            try
            {
                var r = new Rig(first);
                for (int i = 0; i < 2; i++)
                {
                    r.Runner.PrepareNext();
                    r.Pump();
                    r.Runner.StartTrial();
                    r.Runner.EndTrial();
                }
                Assert.AreEqual(2, ProgressStore.FindResumeIndex(root, "P01", out string from));
                Assert.AreEqual("P01_20261010-090000", from);

                var resumed = new Rig(null, start: 2);
                resumed.Runner.PrepareNext();
                Assert.AreEqual(2, resumed.Runner.TrialIndex);
                Assert.AreEqual(0, ProgressStore.FindResumeIndex(root, "P02", out _));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Plan_ShowsDoneCurrentAndUpcoming()
        {
            var r = new Rig();
            Assert.IsTrue(r.Runner.Plan.All(p => p.State == PlanItemState.Upcoming));
            r.Runner.PrepareNext();
            r.Pump();
            r.Runner.StartTrial();
            r.Runner.EndTrial();
            r.Runner.PrepareNext();
            r.Pump();
            var plan = r.Runner.Plan;
            Assert.AreEqual(PlanItemState.Done, plan[0].State);
            Assert.AreEqual(PlanItemState.Current, plan[1].State);
            Assert.AreEqual(PlanItemState.Upcoming, plan[2].State);
            Assert.AreEqual("B", plan[1].Condition.ConditionId);

            r.Runner.StartTrial();
            r.Runner.AbortTrial();
            Assert.AreEqual(PlanItemState.Upcoming, r.Runner.Plan[1].State, "an aborted trial is still to do");
        }

        [Test]
        public void AfterLastEntry_SessionFinishes()
        {
            var r = new Rig();
            for (int i = 0; i < 3; i++)
            {
                r.Runner.PrepareNext();
                r.Pump();
                r.Runner.StartTrial();
                r.Runner.EndTrial();
            }
            r.Runner.PrepareNext();
            Assert.AreEqual(TrialState.Finished, r.Runner.State);
        }
    }
}
