using CoGaze.Core;
using CoGaze.Transport.Loopback;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class LoopbackTests
    {
        static Condition C(string id) => new Condition(id, "beam", Visualization.Circle, true, SigmaSource.Fixed, 3f, "default", "t");

        static bool Next(LoopbackTransport t, MessageKind kind, out ControlMessage m)
        {
            while (t.TryReceive(out m))
                if (m.Kind == kind) return true;
            return false;
        }

        [Test]
        public void Condition_IsAcknowledgedWithSameId()
        {
            var t = new LoopbackTransport(new FakeClock());
            t.Send(ControlMessage.Of(C("C01")));
            Assert.IsTrue(Next(t, MessageKind.ConditionAck, out var ack));
            Assert.AreEqual("C01", ack.Text);
        }

        [Test]
        public void MismatchMode_AcksDifferentId()
        {
            var t = new LoopbackTransport(new FakeClock()) { AckMismatch = true };
            t.Send(ControlMessage.Of(C("C01")));
            Assert.IsTrue(Next(t, MessageKind.ConditionAck, out var ack));
            Assert.AreNotEqual("C01", ack.Text);
        }

        [Test]
        public void Status_IsReportedOncePerSecond()
        {
            var clock = new FakeClock();
            var t = new LoopbackTransport(clock);
            Assert.IsTrue(Next(t, MessageKind.WorkerStatus, out _));
            Assert.IsFalse(Next(t, MessageKind.WorkerStatus, out _));
            clock.AdvanceMs(1001);
            Assert.IsTrue(Next(t, MessageKind.WorkerStatus, out _));
        }

        [Test]
        public void Ping_IsAnsweredWithPong()
        {
            var t = new LoopbackTransport(new FakeClock { NowNs = 1000 }) { WorkerClockOffsetNs = 500 };
            t.Send(ControlMessage.Of(new ClockPing(1, 1000)));
            Assert.IsTrue(Next(t, MessageKind.ClockPing, out var pong));
            Assert.IsTrue(pong.Ping.IsPong);
            Assert.AreEqual(1500, pong.Ping.TReceiveNs);
        }

        [Test]
        public void Cues_PassThroughCodec()
        {
            var t = new LoopbackTransport(new FakeClock());
            t.Send(new GazeCue(5, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 1, 2f, 3));
            Assert.AreEqual(1, t.CuesReceived);
            Assert.AreEqual(5u, t.LastCue.Seq);
        }
    }
}
