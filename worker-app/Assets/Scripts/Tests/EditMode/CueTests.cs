using System.Collections.Generic;
using System.Numerics;
using CoGaze.Core;
using CoGaze.Worker.Cue;
using NUnit.Framework;

namespace CoGaze.Worker.Tests
{
    public class CueTests
    {
        sealed class Clock : IClock { public long NowNs { get; set; } }

        sealed class Log : ILogSink
        {
            public readonly List<string> Rows = new List<string>();
            public void Write(string table, string row) => Rows.Add(row);
        }

        sealed class CueQueue : ICueReceiver
        {
            public readonly Queue<GazeCue> Q = new Queue<GazeCue>();
            public bool TryReceive(out GazeCue cue) => Q.Count > 0 ? Take(out cue) : None(out cue);
            bool Take(out GazeCue c) { c = Q.Dequeue(); return true; }
            static bool None(out GazeCue c) { c = default; return false; }
        }

        sealed class Floor : IWorkSurface
        {
            public AlignmentState State => AlignmentState.Aligned;
            public bool TryGetPlane(out WorkPlane p)
            {
                p = new WorkPlane(new Vector3(0, -1, 0), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
                return true;
            }
        }

        sealed class Spy : ICueRenderer
        {
            public Spy(Visualization k) { Kind = k; }
            public Visualization Kind { get; }
            public bool Visible;
            public CueVisual Last;
            public void Show(in CueVisual v) { Visible = true; Last = v; }
            public void Hide() => Visible = false;
        }

        // Camera at the origin looking down (-Y in Unity), 90° vertical FOV, square 1000 px image.
        static CameraFrame LookingDown(long id) => new CameraFrame(id, id * 1_000_000, Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(System.Math.PI / 2)), 500, 500, 500, 500, 1000, 1000);

        [Test]
        public void CentreOfImage_HitsPlaneStraightAhead()
        {
            CueMath.Unproject(LookingDown(1), 0.5f, 0.5f, out var o, out var d);
            Assert.IsTrue(CueMath.Intersect(o, d, new WorkPlane(new Vector3(0, -1, 0), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ), out var hit, out float dist));
            Assert.AreEqual(0f, hit.X, 1e-5);
            Assert.AreEqual(-1f, hit.Y, 1e-5);
            Assert.AreEqual(0f, hit.Z, 1e-5);
            Assert.AreEqual(1f, dist, 1e-5);
        }

        [Test]
        public void ImageTop_MapsAwayFromImageBottom()
        {
            // OpenCV v grows downward; Unity y grows upward. With the camera pitched down, the top of
            // the image (v=0) must land further forward (+Z) than the bottom (v=1).
            var plane = new WorkPlane(new Vector3(0, -1, 0), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
            CueMath.Unproject(LookingDown(1), 0.5f, 0.25f, out var o, out var dTop);
            CueMath.Unproject(LookingDown(1), 0.5f, 0.75f, out _, out var dBottom);
            CueMath.Intersect(o, dTop, plane, out var top, out _);
            CueMath.Intersect(o, dBottom, plane, out var bottom, out _);
            Assert.Greater(top.Z, bottom.Z);
            Assert.AreEqual(0.5f, top.Z, 1e-5);
        }

        [Test]
        public void RadiusFollowsSigma()
        {
            Assert.AreEqual(0.0524f, CueMath.RadiusForSigma(1f, 3f), 1e-4);
        }

        [Test]
        public void PoseHistory_FindsRecentFramesOnly()
        {
            var h = new FramePoseHistory(capacity: 1000, retentionS: 5f);
            for (long i = 0; i < 600; i++)
                h.Add(new CameraFrame(i, i * 10_000_000L, Vector3.Zero, Quaternion.Identity, 1, 1, 0, 0, 1, 1));   // 100 Hz
            Assert.IsTrue(h.TryGet(599, out _));
            Assert.IsTrue(h.TryGet(100, out _));
            Assert.IsFalse(h.TryGet(50, out _), "older than 5 s");
        }

        (CuePipeline p, CueQueue q, ReceivedCondition c, Spy[] spies, Log log, Clock clock) Rig()
        {
            var poses = new FramePoseHistory(64);
            poses.Add(LookingDown(1));
            var spies = new[] { new Spy(Visualization.Ray), new Spy(Visualization.Circle), new Spy(Visualization.Frustum) };
            var q = new CueQueue();
            var c = new ReceivedCondition();
            var log = new Log();
            var clock = new Clock { NowNs = 1_000_000_000 };
            return (new CuePipeline(q, c, poses, new Floor(), spies, log, clock, new CueStyle(2f)), q, c, spies, log, clock);
        }

        static Condition Cond(string source, Visualization v, bool show) =>
            new Condition("C", source, v, show, SigmaSource.Fixed, 3f, "default", "t");

        [Test]
        public void ShowUncertainty_SizesBySigma_OtherwiseDefault()
        {
            var r = Rig();
            r.c.Apply(Cond("beam", Visualization.Circle, show: true));
            r.q.Q.Enqueue(new GazeCue(1, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 1, 6f, 0));
            r.p.Tick(0.016f);
            Assert.IsTrue(r.spies[1].Visible);
            Assert.IsFalse(r.spies[0].Visible || r.spies[2].Visible);
            Assert.AreEqual(CueMath.RadiusForSigma(1f, 6f), r.spies[1].Last.RadiusM, 1e-5);

            r.c.Apply(Cond("beam", Visualization.Circle, show: false));
            r.q.Q.Enqueue(new GazeCue(2, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 1, 6f, 0));
            r.p.Tick(0.016f);
            Assert.AreEqual(CueMath.RadiusForSigma(1f, 2f), r.spies[1].Last.RadiusM, 1e-5);
        }

        [Test]
        public void NoneCondition_NeverShows()
        {
            var r = Rig();
            r.c.Apply(Cond(Condition.NoSource, Visualization.Circle, show: true));
            r.q.Q.Enqueue(new GazeCue(1, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 1, 3f, 0));
            r.p.Tick(0.016f);
            foreach (var s in r.spies) Assert.IsFalse(s.Visible);
        }

        [Test]
        public void InvalidCue_FadesOutOverFadeTime()
        {
            var r = Rig();
            r.c.Apply(Cond("beam", Visualization.Ray, show: false));
            r.q.Q.Enqueue(new GazeCue(1, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 1, 3f, 0));
            r.p.Tick(0.016f);
            r.q.Q.Enqueue(new GazeCue(2, "beam", false, GazeMode.Eye, 0.5f, 0.5f, 1, 3f, 0));
            r.p.Tick(0.1f);
            Assert.IsTrue(r.spies[0].Visible);
            Assert.AreEqual(0.5f, r.spies[0].Last.Opacity, 1e-4);
            r.p.Tick(0.1f);
            Assert.IsFalse(r.spies[0].Visible);
        }

        [Test]
        public void UnknownFrame_FallsBackToLatestPose_AndLogsIt()
        {
            var r = Rig();
            r.c.Apply(Cond("beam", Visualization.Circle, show: false));
            r.q.Q.Enqueue(new GazeCue(1, "beam", true, GazeMode.Eye, 0.5f, 0.5f, 999, 3f, 0));
            r.p.Tick(0.016f);
            Assert.IsTrue(r.spies[1].Visible);
            StringAssert.StartsWith("1000000000,1,beam,1,1,1,", r.log.Rows[0]);
        }
    }
}
