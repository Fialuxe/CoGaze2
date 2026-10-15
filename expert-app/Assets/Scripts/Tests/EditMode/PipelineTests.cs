using System.Collections.Generic;
using System.Linq;
using CoGaze.Core;
using CoGaze.Expert.Pipeline;
using CoGaze.Expert.Sources.Fake;
using CoGaze.Expert.View2D;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    public class PipelineTests
    {
        TestClock _clock;
        MemoryLog _log;
        RecordingSender _sender;
        FixedCondition _cond;
        StubCalibration _calib;
        static readonly VideoRect Rect = new VideoRect(0, 0, 1000, 1000);
        static readonly Dictionary<string, FilterSettings> Filters = new Dictionary<string, FilterSettings>
        {
            ["default"] = new FilterSettings(true, 1f, 0.007f, 1f),
            ["none"] = FilterSettings.PassThrough,
        };

        [SetUp]
        public void SetUp()
        {
            _clock = new TestClock { NowNs = 1_000_000_000 };
            _log = new MemoryLog();
            _sender = new RecordingSender();
            _cond = new FixedCondition();
            _calib = new StubCalibration();
        }

        GazePipeline Build(IReadOnlyList<IGazeSource> sources, float rateHz = 30f)
        {
            var screen = new ScreenGeometry(1920, 527f, 650f);
            var sigma = new ConditionSigmaPolicy(_cond, new FixedSigmaPolicy(_cond), new CalibSigmaPolicy(_calib, 3f),
                                                 new CalibLiveSigmaPolicy(_calib, 3f, screen, 10));
            return new GazePipeline(sources, _cond, new View2DProjector(Rect, new DisplayedFrameLog()), sigma, _sender,
                                    _log, _clock, Filters, rateHz);
        }

        List<IGazeSource> ThreeFakes() => new List<IGazeSource>
        {
            new FakeGazeSource("beam", _clock, 500, 500, 300, 300, 60, seed: 1),
            new FakeGazeSource("tobii", _clock, 500, 500, 300, 300, 60, seed: 2, offsetPx: 40),
            new FakeGazeSource("webcam:mobilegaze", _clock, 500, 500, 300, 300, 60, seed: 3),
        };

        void Run(GazePipeline p, int frames, double frameMs = 16.7)
        {
            for (int i = 0; i < frames; i++)
            {
                _clock.AdvanceMs(frameMs);
                p.Tick();
            }
        }

        [TestCase("beam")]
        [TestCase("tobii")]
        [TestCase("webcam:mobilegaze")]
        public void VT01_OnlyTheDisplaySourceIsEverSent(string display)
        {
            _cond.Current = Conditions.Make("C1", display);
            var p = Build(ThreeFakes());
            Run(p, 300);
            Assert.Greater(_sender.Sent.Count, 0);
            Assert.IsTrue(_sender.Sent.All(c => c.Source == display));
            // every source was still received and logged (EXP-F-032)
            Assert.AreEqual(3, p.Received.Count);
            var rows = _log.Of(LogSchema.Display);
            Assert.IsTrue(rows.Any(r => r.Contains(",tobii,")));
            Assert.IsFalse(_log.Of(LogSchema.Events).Any(r => r.Contains(EventKinds.SourceMismatch)));
        }

        [Test]
        public void VT01_RemovingAnotherSourceDoesNotChangeTheSendCount()
        {
            _cond.Current = Conditions.Make("C1", "webcam:mobilegaze");
            var all = Build(ThreeFakes());
            Run(all, 300);
            int withTobii = _sender.Sent.Count;

            SetUp();
            _cond.Current = Conditions.Make("C1", "webcam:mobilegaze");
            var withoutTobii = Build(ThreeFakes().Where(s => s.Name != "tobii").ToList());
            Run(withoutTobii, 300);
            Assert.AreEqual(withTobii, _sender.Sent.Count);
        }

        [Test]
        public void NoneCondition_SendsNothing_ButStillLogs()
        {
            _cond.Current = Conditions.Make("C0", Condition.NoSource);
            var p = Build(ThreeFakes());
            Run(p, 300);
            Assert.AreEqual(0, _sender.Sent.Count);
            Assert.Greater(_log.Of(LogSchema.Display).Count, 0);
        }

        [Test]
        public void NoCondition_SendsNothing()
        {
            _cond.HasCondition = false;
            var p = Build(ThreeFakes());
            Run(p, 100);
            Assert.AreEqual(0, _sender.Sent.Count);
        }

        [Test]
        public void SendRate_NeverExceedsConfiguredHz()
        {
            _cond.Current = Conditions.Make("C1", "beam");
            var src = new ScriptedSource("beam");
            var p = Build(new[] { src }, rateHz: 30f);
            for (int i = 0; i < 1000; i++)   // 1 kHz input for one second
            {
                _clock.AdvanceMs(1);
                src.Push(_clock.NowNs, 500, 500);
                p.Tick();
            }
            Assert.LessOrEqual(_sender.Sent.Count, 31);
            Assert.GreaterOrEqual(_sender.Sent.Count, 29);
        }

        [Test]
        public void Sigma_FixedCalibAndCalibLive()
        {
            var src = new ScriptedSource("beam");
            var p = Build(new[] { src });

            _cond.Current = Conditions.Make("F", "beam", SigmaSource.Fixed, fixedSigma: 4.5f);
            src.Push(_clock.NowNs, 500, 500);
            _clock.AdvanceMs(100);
            p.Tick();
            Assert.AreEqual(4.5f, _sender.Sent.Last().SigmaDeg);

            _cond.Current = Conditions.Make("C", "beam", SigmaSource.Calib, float.NaN);
            src.Push(_clock.NowNs, 500, 500);
            _clock.AdvanceMs(100);
            p.Tick();
            Assert.AreEqual(3f, _sender.Sent.Last().SigmaDeg, "default before calibration");

            _calib.HasResult = true;
            _calib.MeanValidationErrorDeg = 1.8f;
            src.Push(_clock.NowNs, 500, 500);
            _clock.AdvanceMs(100);
            p.Tick();
            Assert.AreEqual(1.8f, _sender.Sent.Last().SigmaDeg);

            _cond.Current = Conditions.Make("L", "beam", SigmaSource.CalibLive, float.NaN);
            src.Push(_clock.NowNs, 500, 500, confidence: 3);
            _clock.AdvanceMs(100);
            p.Tick();
            float steady = _sender.Sent.Last().SigmaDeg;
            Assert.AreEqual(1.8f, steady, 1e-4, "no dispersion yet: equals calib");

            src.Push(_clock.NowNs, 900, 100, confidence: 1);
            _clock.AdvanceMs(100);
            p.Tick();
            Assert.Greater(_sender.Sent.Last().SigmaDeg, steady * 1.5f, "dispersion and low confidence widen sigma");
        }

        [Test]
        public void Sigma_IsLoggedEvenWhenNotShown()
        {
            _cond.Current = Conditions.Make("C1", "beam", show: false, fixedSigma: 2.5f);
            var src = new ScriptedSource("beam");
            var p = Build(new[] { src });
            src.Push(_clock.NowNs, 500, 500);
            _clock.AdvanceMs(100);
            p.Tick();
            StringAssert.Contains(",0,fixed,2.5,1,beam,", _log.Of(LogSchema.Display).Last());
        }

        [Test]
        public void OutsideTheVideo_IsSentInvalid()
        {
            _cond.Current = Conditions.Make("C1", "beam");
            var src = new ScriptedSource("beam");
            var p = Build(new[] { src });
            src.Push(_clock.NowNs, 1500, 500);
            _clock.AdvanceMs(100);
            p.Tick();
            Assert.IsFalse(_sender.Sent.Single().Valid);
        }

        [Test]
        public void OneEuro_SmoothsNoiseAndFollowsSteps()
        {
            var f = new OneEuroFilter(new FilterSettings(true, 1f, 0.007f, 1f));
            var rng = new System.Random(1);
            float last = 0;
            for (int i = 0; i < 120; i++)
                last = f.Apply(100f + (float)(rng.NextDouble() - 0.5) * 20f, i * 16_666_667L);
            Assert.AreEqual(100f, last, 3f);
            for (int i = 120; i < 240; i++) last = f.Apply(400f, i * 16_666_667L);
            Assert.AreEqual(400f, last, 2f);
        }
    }
}
