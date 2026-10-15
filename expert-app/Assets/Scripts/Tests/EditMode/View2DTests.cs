using CoGaze.Core;
using CoGaze.Expert.View2D;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    /// <summary>#22: the video sits aspect-preserved in the fixed stage, and gaze uv is relative to the picture.</summary>
    public class View2DTests
    {
        static readonly VideoRect Stage = new VideoRect(160, 90, 1600, 900);

        [Test]
        public void FourByThreeVideo_IsPillarboxedAndCentred()
        {
            var r = Stage.FitInside(1280, 960);
            Assert.AreEqual(1200f, r.WidthPx, 1e-3);
            Assert.AreEqual(900f, r.HeightPx, 1e-3);
            Assert.AreEqual(160f + 200f, r.XPx, 1e-3);
            Assert.AreEqual(90f, r.YPx, 1e-3);
        }

        [Test]
        public void WideVideo_IsLetterboxed()
        {
            var r = new VideoRect(0, 0, 1000, 1000).FitInside(2000, 1000);
            Assert.AreEqual(1000f, r.WidthPx, 1e-3);
            Assert.AreEqual(500f, r.HeightPx, 1e-3);
            Assert.AreEqual(250f, r.YPx, 1e-3);
        }

        [Test]
        public void UnknownSize_KeepsTheStage()
        {
            var r = Stage.FitInside(0, 0);
            Assert.AreEqual(Stage.WidthPx, r.WidthPx);
            Assert.AreEqual(Stage.XPx, r.XPx);
        }

        [Test]
        public void CentreOfThePicture_IsHalfHalf_AndThePillarboxIsOutside()
        {
            var frames = new DisplayedFrameLog();
            frames.Record(0, 17);
            var p = new View2DProjector(Stage, frames);
            p.SetRect(Stage.FitInside(1280, 960));

            Assert.IsTrue(p.TryProject(new GazeSample("beam", 5, 5, true, 960, 540), out var ray));
            Assert.AreEqual(0.5f, ray.U, 1e-4);
            Assert.AreEqual(0.5f, ray.V, 1e-4);
            Assert.AreEqual(17, ray.VideoFrameId);

            Assert.IsFalse(p.TryProject(new GazeSample("beam", 5, 5, true, 200, 540), out _), "left pillarbox is not video");
        }
    }
}
