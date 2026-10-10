using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CoGaze.Core;
using CoGaze.Worker.Capture;
using CoGaze.Worker.Cue;
using CoGaze.Worker.Session;
using NUnit.Framework;

namespace CoGaze.Worker.Tests
{
    /// <summary>#21 capture bookkeeping and #26 live worker behaviour, without Unity or a headset.</summary>
    public class SessionTests
    {
        sealed class Clock : IClock { public long NowNs { get; set; } }

        sealed class Log : ILogSink
        {
            public readonly List<(string table, string row)> Rows = new List<(string, string)>();
            public void Write(string table, string row) => Rows.Add((table, row));
            public IEnumerable<string> Of(string table) => Rows.Where(r => r.table == table).Select(r => r.row);
        }

        sealed class Channel : IMessageChannel
        {
            public readonly Queue<ControlMessage> In = new Queue<ControlMessage>();
            public readonly List<ControlMessage> Out = new List<ControlMessage>();
            public bool IsConnected => true;
            public void Send(in ControlMessage message) => Out.Add(message);
            public bool TryReceive(out ControlMessage message)
            {
                bool any = In.Count > 0;
                message = any ? In.Dequeue() : default;
                return any;
            }
        }

        static CameraFrame Frame(long id, long tNs) =>
            new CameraFrame(id, tNs, new Vector3(1, 2, 3), Quaternion.Identity, 500, 501, 640, 480, 1280, 960);

        [Test]
        public void Recorder_NumbersFramesFromOne_LogsSchemaColumns_AndFillsHistory()
        {
            var log = new Log();
            var history = new FramePoseHistory(capacity: 16);
            var rec = new FrameRecorder(history, log);
            long a = rec.NextId(), b = rec.NextId();
            Assert.AreEqual(1, a);
            Assert.AreEqual(2, b);

            rec.Record(Frame(a, 100));
            var row = log.Of(LogSchema.Frames).Single().Split(',');
            Assert.AreEqual(LogSchema.ColumnsOf(LogSchema.Frames).Length, row.Length);
            Assert.AreEqual("1", row[0]);
            Assert.AreEqual("100", row[1]);
            Assert.AreEqual("1280", row[13]);
            Assert.IsTrue(history.TryGet(1, out var back));
            Assert.AreEqual(500f, back.Fx);
        }

        [Test]
        public void History_KeepsFiveSecondsOfFrames()
        {
            var history = new FramePoseHistory(capacity: 512);
            for (long i = 1; i <= 300; i++) history.Add(Frame(i, i * 33_333_333L));   // 10 s at 30 fps
            Assert.IsTrue(history.TryGet(300 - 140, out _), "4.7 s old");
            Assert.IsFalse(history.TryGet(300 - 160, out _), "5.3 s old");
        }

        [Test]
        public void Responder_EchoesCondition_AppliesTrial_AnswersPing_AndOpensSession()
        {
            var ch = new Channel();
            var clock = new Clock { NowNs = 5_000 };
            var log = new Log();
            var conditions = new List<string>();
            var trials = new List<int>();
            string session = null;
            var r = new ControlResponder(ch, clock, log, c => conditions.Add(c.ConditionId), t => trials.Add(t.TrialIndex), s => session = s);

            ch.In.Enqueue(ControlMessage.Session("P01_20261010-120000"));
            ch.In.Enqueue(ControlMessage.Of(new Condition("C04", "beam", Visualization.Circle, true, SigmaSource.Fixed, 3f, "default", "t")));
            ch.In.Enqueue(ControlMessage.Of(new TrialMessage(2, TrialPhase.Start, TrialKind.Main, "C04", "t", "")));
            ch.In.Enqueue(ControlMessage.Of(new ClockPing(9, 1_000)));
            ch.In.Enqueue(ControlMessage.Of(new ClockPing(9, 1_000, 2_000, 2_001)));   // a pong is not answered
            r.Drain();

            Assert.AreEqual("P01_20261010-120000", session);
            CollectionAssert.AreEqual(new[] { "C04" }, conditions);
            CollectionAssert.AreEqual(new[] { 2 }, trials);
            Assert.AreEqual(2, ch.Out.Count);
            Assert.AreEqual(MessageKind.ConditionAck, ch.Out[0].Kind);
            Assert.AreEqual("C04", ch.Out[0].Text);
            var pong = ch.Out[1].Ping;
            Assert.IsTrue(pong.IsPong);
            Assert.AreEqual(9u, pong.Seq);
            Assert.AreEqual(1_000, pong.TPingNs);
            Assert.AreEqual(5_000, pong.TReceiveNs);
            Assert.GreaterOrEqual(pong.TReplyNs, pong.TReceiveNs);
            Assert.IsTrue(log.Of(LogSchema.Events).Any(e => e.Contains(EventKinds.ConditionAck)));
            Assert.IsTrue(log.Of(LogSchema.Events).Any(e => e.Contains(EventKinds.TrialStart)));
        }

        [Test]
        public void Status_GoesOutOncePerSecond_WithTheMeasuredFrameRate()
        {
            var ch = new Channel();
            var clock = new Clock();
            var s = new StatusReporter(ch, clock);
            for (int i = 0; i <= 72; i++)
            {
                clock.NowNs = i * 1_000_000_000L / 72;
                s.Tick(AlignmentState.Aligned, recording: true);
            }
            Assert.AreEqual(2, ch.Out.Count, "at t=0 and at t=1 s");
            Assert.AreEqual(72f, ch.Out[1].Status.FrameRateHz, 1f);
            Assert.AreEqual(AlignmentState.Aligned, ch.Out[1].Status.Alignment);
            Assert.IsTrue(ch.Out[1].Status.Recording);
        }

        [Test]
        public void PoseLog_WritesAt30Hz_WithEmptyFieldsForUntrackedHands()
        {
            var log = new Log();
            var p = new PoseLogger(log, rateHz: 30f);
            var head = new TrackedPose(true, new Vector3(0, 1.6f, 0), Quaternion.Identity);
            for (int i = 0; i < 90; i++) p.Tick(i * 1_000_000_000L / 90, head, TrackedPose.None, TrackedPose.None);   // 1 s at 90 fps
            var rows = log.Of(LogSchema.Pose).ToList();
            Assert.AreEqual(30, rows.Count);
            var f = rows[0].Split(',');
            Assert.AreEqual(LogSchema.ColumnsOf(LogSchema.Pose).Length, f.Length);
            Assert.AreEqual("1.6", f[2]);
            Assert.AreEqual("", f[8], "left hand untracked");
        }

        [Test]
        public void ForwardingSink_DropsUntilAttached()
        {
            var sink = new ForwardingLogSink();
            sink.Write(LogSchema.Events, "1,a,");
            var log = new Log();
            sink.Attach(log);
            sink.Write(LogSchema.Events, "2,b,");
            Assert.AreEqual(1, log.Rows.Count);
            Assert.AreEqual(1, sink.Dropped);
        }
    }
}
