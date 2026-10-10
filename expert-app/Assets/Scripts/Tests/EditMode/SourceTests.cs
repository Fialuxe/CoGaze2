using System.Collections.Generic;
using CoGaze.Core;
using CoGaze.Expert.Sources.Fake;
using CoGaze.Expert.Sources.Osc;
using CoGaze.Expert.View2D;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    public class SourceTests
    {
        [Test]
        public void Fake_IsDeterministic()
        {
            List<GazeSample> Run()
            {
                var clock = new TestClock();
                var src = new FakeGazeSource("beam", clock, 500, 500, 200, 200, 30, seed: 7, dropout: 0.1);
                var list = new List<GazeSample>();
                for (int i = 0; i < 100; i++)
                {
                    clock.AdvanceMs(16);
                    while (src.TryRead(out var s)) list.Add(s);
                }
                return list;
            }
            var a = Run();
            var b = Run();
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].XPx, b[i].XPx);
                Assert.AreEqual(a[i].Valid, b[i].Valid);
            }
            Assert.That(a.Count, Is.InRange(46, 50));
        }

        static OscRouter Route(byte[] packet, TestClock clock)
        {
            var router = new OscRouter(clock);
            var list = new List<OscMessage>();
            Assert.IsTrue(OscMessage.TryParse(packet, packet.Length, list));
            router.Route(list);
            return router;
        }

        [Test]
        public void Osc_Tobii()
        {
            var r = Route(OscMessage.Encode("/cogaze/tobii", 123L, 640.5f, 360.25f, 1), new TestClock { NowNs = 999 });
            Assert.IsTrue(r.Tobii.TryDequeue(out var s));
            Assert.AreEqual("tobii", s.Source);
            Assert.AreEqual(123L, s.TCaptureNs);
            Assert.AreEqual(999L, s.TReceiveNs);
            Assert.AreEqual(640.5f, s.XPx);
            Assert.IsTrue(s.Valid);
        }

        [Test]
        public void Osc_Beam_ConfidenceZeroIsInvalid()
        {
            var r = Route(OscMessage.Encode("/cogaze/beam", 1L, 10f, 20f, 0f, 0f, 0f, 0f, 0f, 0f, 0), new TestClock());
            Assert.IsTrue(r.Beam.TryDequeue(out var s));
            Assert.IsFalse(s.Valid);
            Assert.AreEqual(0, s.Confidence);
        }

        [Test]
        public void Osc_Gaze_AllNineteenFields()
        {
            var r = Route(OscMessage.Encode("/cogaze/gaze", 10L, 11L, 12L, "webcam:mobilegaze", 1, "blend",
                0f, 0f, 1f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 2.5f), new TestClock());
            Assert.IsTrue(r.Estimator.TryDequeue(out var e));
            Assert.AreEqual(12L, e.FrameIdx);
            Assert.AreEqual("blend", e.Mode);
            Assert.AreEqual(1f, e.Gaze.Z);
            Assert.AreEqual(9f, e.HeadPosMm.Z);
            Assert.AreEqual(2.5f, e.SigmaDeg);
        }

        [Test]
        public void Osc_StatusTimesOut()
        {
            var clock = new TestClock { NowNs = 1_000_000_000 };
            var r = Route(OscMessage.Encode("/cogaze/status", "rec", 5L, "recording", 29.9f), clock);
            Assert.IsTrue(r.Status.TryGet("rec", out var e));
            Assert.AreEqual(29.9f, e.RateHz);
            Assert.IsTrue(e.IsAlive(clock.NowNs + 2_900_000_000, 3_000_000_000));
            Assert.IsFalse(e.IsAlive(clock.NowNs + 3_100_000_000, 3_000_000_000));
        }

        [Test]
        public void Osc_WrongArityIsCountedNotThrown()
        {
            var r = Route(OscMessage.Encode("/cogaze/tobii", 1L), new TestClock());
            Assert.AreEqual(1, r.Malformed);
            Assert.IsFalse(OscMessage.TryParse(new byte[] { 1, 2, 3 }, 3, new List<OscMessage>()));
        }

        [Test]
        public void View2D_CentreIsHalfHalf_OutsideIsInvalid()
        {
            var frames = new DisplayedFrameLog();
            frames.Record(100, 41);
            frames.Record(200, 42);
            var p = new View2DProjector(new VideoRect(160, 90, 1600, 900), frames);

            Assert.IsTrue(p.TryProject(new GazeSample("beam", 250, 250, true, 960, 540), out var ray));
            Assert.AreEqual(0.5f, ray.U, 1e-6);
            Assert.AreEqual(0.5f, ray.V, 1e-6);
            Assert.AreEqual(42, ray.VideoFrameId);

            Assert.IsTrue(p.TryProject(new GazeSample("beam", 150, 150, true, 160, 90), out ray));
            Assert.AreEqual(0f, ray.U);
            Assert.AreEqual(41, ray.VideoFrameId, "the frame on screen when the sample was captured");

            Assert.IsFalse(p.TryProject(new GazeSample("beam", 250, 250, true, 100, 540), out ray));
            Assert.IsFalse(ray.Valid);
        }

        [Test]
        public void ScreenGeometry_OneDegree()
        {
            var g = new ScreenGeometry(1920, 527f, 650f);
            Assert.AreEqual(1f, g.PxToDeg(g.PxPerDeg), 1e-3);
        }
    }
}
