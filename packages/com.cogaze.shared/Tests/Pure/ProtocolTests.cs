using CoGaze.Core;
using CoGaze.Protocol;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    public class ProtocolTests
    {
        [Test]
        public void GazeCue_RoundTrips()
        {
            var cue = new GazeCue(42, "beam", true, GazeMode.Blend, 0.25f, 0.75f, 9001, 2.5f, 123456789);
            Assert.IsTrue(MessageCodec.IsCue(MessageCodec.Encode(cue)));
            Assert.IsTrue(MessageCodec.TryDecode(MessageCodec.Encode(cue), out GazeCue back));
            Assert.AreEqual(cue.Seq, back.Seq);
            Assert.AreEqual(cue.Source, back.Source);
            Assert.AreEqual(cue.Mode, back.Mode);
            Assert.AreEqual(cue.U, back.U);
            Assert.AreEqual(cue.V, back.V);
            Assert.AreEqual(cue.VideoFrameId, back.VideoFrameId);
            Assert.AreEqual(cue.SigmaDeg, back.SigmaDeg);
            Assert.AreEqual(cue.TExpNs, back.TExpNs);
        }

        static ControlMessage RoundTrip(ControlMessage m)
        {
            Assert.IsFalse(MessageCodec.IsCue(MessageCodec.Encode(m)));
            Assert.IsTrue(MessageCodec.TryDecode(MessageCodec.Encode(m), out ControlMessage back));
            Assert.AreEqual(m.Kind, back.Kind);
            return back;
        }

        [Test]
        public void EveryControlMessage_RoundTrips()
        {
            var c = RoundTrip(ControlMessage.Of(new Condition("C04", "beam", Visualization.Circle, true, SigmaSource.Fixed, 3f, "default", "identify_A"))).Condition;
            Assert.AreEqual("C04", c.ConditionId);
            Assert.AreEqual(Visualization.Circle, c.Visualization);
            Assert.AreEqual(3f, c.FixedSigmaDeg);

            Assert.AreEqual("C04", RoundTrip(ControlMessage.Ack("C04")).Text);
            Assert.AreEqual("P01_x", RoundTrip(ControlMessage.Session("P01_x")).Text);

            var t = RoundTrip(ControlMessage.Of(new TrialMessage(3, TrialPhase.Start, TrialKind.Practice, "C04", "identify_A", "{\"a\":1}"))).Trial;
            Assert.AreEqual(3, t.TrialIndex);
            Assert.AreEqual(TrialKind.Practice, t.Kind);
            Assert.AreEqual("{\"a\":1}", t.LayoutJson);

            var s = RoundTrip(ControlMessage.Of(new WorkerStatus(72f, AlignmentState.TrackingLost, true, 99))).Status;
            Assert.AreEqual(AlignmentState.TrackingLost, s.Alignment);

            var e = RoundTrip(ControlMessage.Of(new WorkerEvent(5, "select", "target_2"))).Event;
            Assert.AreEqual("target_2", e.Detail);

            var p = RoundTrip(ControlMessage.Of(new ClockPing(7, 1, 2, 3))).Ping;
            Assert.IsTrue(p.IsPong);
            Assert.AreEqual(3, p.TReplyNs);
        }

        [Test]
        public void Malformed_ReturnsFalse()
        {
            Assert.IsFalse(MessageCodec.TryDecode(null, out GazeCue _));
            Assert.IsFalse(MessageCodec.TryDecode(new byte[] { 1, 2 }, out GazeCue _));
            var bytes = MessageCodec.Encode(new GazeCue(1, "beam", true, GazeMode.Eye, 0, 0, 0, 0, 0));
            System.Array.Resize(ref bytes, bytes.Length - 3);
            Assert.IsFalse(MessageCodec.TryDecode(bytes, out GazeCue _));
            Assert.IsFalse(MessageCodec.TryDecode(new byte[] { MessageCodec.Magic, MessageCodec.Version, 200 }, out ControlMessage _));
        }
    }
}
