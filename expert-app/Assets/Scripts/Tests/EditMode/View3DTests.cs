using System;
using System.IO;
using System.Numerics;
using CoGaze.Core;
using CoGaze.Expert.View3D.Model;
using CoGaze.Logging;
using NUnit.Framework;

namespace CoGaze.Expert.Tests
{
    public class OrbitCameraTests
    {
        static OrbitCamera Camera() => new OrbitCamera(new OrbitPose(new Vector3(0f, 0f, 1f), 0f, 0f, 2f));

        [Test]
        public void Pose_SitsDistanceBehindTarget_AlongForward()
        {
            var p = new OrbitPose(new Vector3(1f, 2f, 3f), 90f, 0f, 2f);
            Assert.AreEqual(1f, p.Forward.X, 1e-5);                  // yaw 90: looking along +X
            Assert.AreEqual(-1f, p.Position.X, 1e-5);
            Assert.AreEqual(2f, p.Position.Y, 1e-5);
            var down = new OrbitPose(Vector3.Zero, 0f, 90f, 1f);    // positive pitch looks down (Unity)
            Assert.AreEqual(-1f, down.Forward.Y, 1e-5);
            Assert.AreEqual(1f, down.Position.Y, 1e-5);
        }

        [Test]
        public void Orbit_TurnsAroundTheTarget_AndClampsPitch()
        {
            var c = Camera();
            c.Orbit(400f, 0f);
            Assert.AreEqual(400f * c.DegPerPx, c.Goal.YawDeg, 1e-4);
            Assert.AreEqual(Vector3.Distance(c.Goal.Position, c.Goal.Target), 2f, 1e-4);
            c.Orbit(0f, -100000f);
            Assert.AreEqual(OrbitCamera.MaxPitchDeg, c.Goal.PitchDeg, 1e-4);
            c.Orbit(0f, 100000f);
            Assert.AreEqual(-OrbitCamera.MaxPitchDeg, c.Goal.PitchDeg, 1e-4);
            Assert.IsTrue(c.UserMoved);
        }

        [Test]
        public void Pan_MovesTargetSideways_ByWhatThePixelsCover()
        {
            var c = Camera();
            c.Pan(100f, 0f, 1000f, 60f);
            // 2 m away with a 60° vertical FOV: 1000 px cover 2·2·tan(30°) = 2.309 m.
            Assert.AreEqual(-0.2309f, c.Goal.Target.X, 1e-3);     // grab-and-drag: the scene follows the mouse
            Assert.AreEqual(0f, c.Goal.Target.Y, 1e-5);
            Assert.AreEqual(1f, c.Goal.Target.Z, 1e-5);
        }

        [Test]
        public void Zoom_ScalesDistance_WithinLimits()
        {
            var c = Camera();
            c.Zoom(1f);
            Assert.AreEqual(2f * c.ZoomPerStep, c.Goal.DistanceM, 1e-5);
            c.Zoom(-1f);
            Assert.AreEqual(2f, c.Goal.DistanceM, 1e-4);
            c.Zoom(1000f);
            Assert.AreEqual(OrbitCamera.MinDistanceM, c.Goal.DistanceM, 1e-6);
            c.Zoom(-1000f);
            Assert.AreEqual(OrbitCamera.MaxDistanceM, c.Goal.DistanceM, 1e-4);
        }

        [Test]
        public void Frame_CentresTheBox_AndFitsItInView()
        {
            var c = Camera();
            c.Frame(new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 2f), 60f, 16f / 9f);
            Assert.AreEqual(0f, Vector3.Distance(c.Goal.Target, new Vector3(0f, 0f, 1f)), 1e-5);
            // radius √3 must fit the 30° half-angle: distance ≥ √3 / sin 30° = 3.46
            Assert.GreaterOrEqual(c.Goal.DistanceM, 3.46f);
            Assert.Less(c.Goal.DistanceM, 4f);
            Assert.IsFalse(c.UserMoved);                            // framing is not the user steering
        }

        [Test]
        public void LookFrom_PutsTheCameraAtThatPoint_FacingThatWay()
        {
            var c = Camera();
            var forward = Vector3.Normalize(new Vector3(0f, -1f, 1f));
            c.LookFrom(new Vector3(0.5f, 1.6f, 0f), forward, 1f);
            Assert.AreEqual(0f, Vector3.Distance(c.Goal.Position, new Vector3(0.5f, 1.6f, 0f)), 1e-4);
            Assert.AreEqual(1f, Vector3.Dot(c.Goal.Forward, forward), 1e-4);
        }

        [Test]
        public void Tick_EasesTowardsTheGoal_AndArrives()
        {
            var c = Camera();
            c.Orbit(400f, 0f);
            c.Tick(1f / 60f);
            Assert.Greater(c.Current.YawDeg, 0f);
            Assert.Less(c.Current.YawDeg, c.Goal.YawDeg);
            for (int i = 0; i < 60; i++) c.Tick(1f / 60f);           // well settled within a second
            Assert.AreEqual(c.Goal.YawDeg, c.Current.YawDeg, 1e-3);
        }
    }

    public class ChunkUpdateTests
    {
        /// <summary>The layout of server/chunks.py Update.encode: "&lt;4sHHII", keys "&lt;Biii", then "&lt;II" + payload + labels.</summary>
        static byte[] Encode(int epoch, (ChunkKey key, byte[] payload, byte[] labels)[] replaced, ChunkKey[] removed, string magic = "CGZM")
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(System.Text.Encoding.ASCII.GetBytes(magic));
            w.Write((ushort)1);
            w.Write((ushort)epoch);
            w.Write((uint)replaced.Length);
            w.Write((uint)removed.Length);
            void Key(ChunkKey k) { w.Write(k.Lod); w.Write(k.I); w.Write(k.J); w.Write(k.K); }
            foreach (var r in replaced) Key(r.key);
            foreach (var k in removed) Key(k);
            foreach (var r in replaced)
            {
                w.Write((uint)r.payload.Length);
                w.Write((uint)r.labels.Length);
                w.Write(r.payload);
                w.Write(r.labels);
            }
            return ms.ToArray();
        }

        [Test]
        public void Parse_ReadsKeysAndPayloads_InServerOrder()
        {
            var a = new ChunkKey(0, -1, 2, 3);
            var b = new ChunkKey(1, 4, -5, 6);
            var gone = new ChunkKey(0, 7, 8, -9);
            var bytes = Encode(3, new[] { (a, new byte[] { 1, 2, 3 }, new byte[] { 0, 0 }), (b, new byte[] { 9 }, new byte[0]) }, new[] { gone });

            var u = ChunkUpdate.Parse(bytes);
            Assert.AreEqual(3, u.Epoch);
            CollectionAssert.AreEqual(new[] { a, b }, u.Replaced);
            CollectionAssert.AreEqual(new[] { gone }, u.Removed);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, u.Payloads[0]);
            CollectionAssert.AreEqual(new byte[] { 9 }, u.Payloads[1]);
            Assert.AreEqual("near_-1_2_3", a.ToString());
            Assert.AreEqual("far_4_-5_6", b.ToString());
        }

        [Test]
        public void Parse_RejectsWhatIsNotAChunkUpdate()
        {
            Assert.Throws<InvalidDataException>(() => ChunkUpdate.Parse(Encode(0, new (ChunkKey, byte[], byte[])[0], new ChunkKey[0], "CGZK")));
            Assert.Throws<InvalidDataException>(() => ChunkUpdate.Parse(new byte[] { 1, 2 }));
        }
    }

    public class ReconstructionHealthTests
    {
        const long S = 1_000_000_000L;

        static HealthItem Row(ReconstructionStatus s, long nowNs)
        {
            var board = new HealthBoard(null, new TestClock { NowNs = nowNs });
            var clock = new TestClock { NowNs = nowNs };
            new ReconstructionHealthReporter(s, board, clock).Tick();
            Assert.IsTrue(board.TryGet(ReconstructionHealth.Key, out var item));
            return item;
        }

        [Test]
        public void NotConfigured_IsOk_SoA2DSessionIsNotBlocked()
        {
            var s = new ReconstructionStatus(new ReconstructionSource("", "", 0.15f));
            Assert.AreEqual(ReconstructionMode.NotConfigured, s.Mode);
            var row = Row(s, 0);
            Assert.AreEqual(HealthLevel.Ok, row.Level);
            StringAssert.Contains("not configured", row.Value);
            StringAssert.Contains("no reconstruction configured", ReconstructionHealth.StageLabel(s, 0));
        }

        [Test]
        public void Live_ConnectedWithChunks_IsOk_AndShowsCountAndAge()
        {
            var s = new ReconstructionStatus(new ReconstructionSource("ws://pc:8765/expert", "", 0.15f));
            s.Link = LinkState.Connecting;
            Assert.AreEqual(HealthLevel.Unknown, Row(s, 0).Level);
            StringAssert.Contains("connecting", ReconstructionHealth.StageLabel(s, 0));

            s.Link = LinkState.Connected;
            StringAssert.Contains("waiting for data", ReconstructionHealth.StageLabel(s, 0));
            s.Applied(chunks: 312, nowNs: 10 * S);
            var row = Row(s, 14 * S);
            Assert.AreEqual(HealthLevel.Ok, row.Level);
            StringAssert.Contains("312 chunks", row.Value);
            StringAssert.Contains("4 s ago", row.Value);
            StringAssert.Contains("LIVE", ReconstructionHealth.StageLabel(s, 14 * S));
        }

        [Test]
        public void Live_Unreachable_IsRed_WithWhatToDo()
        {
            var s = new ReconstructionStatus(new ReconstructionSource("ws://pc:8765/expert", "", 0.15f));
            s.Fail("connection refused");
            var row = Row(s, 0);
            Assert.AreEqual(HealthLevel.Error, row.Level);
            StringAssert.Contains("ws://pc:8765/expert", row.Problem);
            StringAssert.Contains("connection refused", row.Problem);
            StringAssert.Contains("service.py", row.Action);
            StringAssert.Contains("OFFLINE", ReconstructionHealth.StageLabel(s, 0));
        }

        [Test]
        public void Replay_ShowsProgress_ThenDone_AndMissingDataIsRed()
        {
            var s = new ReconstructionStatus(new ReconstructionSource("", "C:/data/chunks", 0.15f));
            Assert.AreEqual(ReconstructionMode.Replay, s.Mode);
            s.Link = LinkState.Replaying;
            s.UpdatesTotal = 9;
            s.Applied(chunks: 40, nowNs: S);
            Assert.AreEqual(HealthLevel.Ok, Row(s, S).Level);
            StringAssert.Contains("1 / 9", ReconstructionHealth.StageLabel(s, S));
            s.Link = LinkState.ReplayDone;
            StringAssert.Contains("finished", ReconstructionHealth.StageLabel(s, S));

            var missing = new ReconstructionStatus(new ReconstructionSource("", "C:/nowhere", 0.15f));
            missing.Fail("no .cgzm files in C:/nowhere");
            var row = Row(missing, 0);
            Assert.AreEqual(HealthLevel.Error, row.Level);
            StringAssert.Contains("make_synthetic_chunks.py", row.Action);
        }

        [Test]
        public void DecodeErrors_AreAWarning_NotSilentlyDropped()
        {
            var s = new ReconstructionStatus(new ReconstructionSource("ws://pc:8765/expert", "", 0.15f)) { Link = LinkState.Connected };
            s.Applied(chunks: 10, nowNs: 0);
            s.DecodeFailures = 2;
            var row = Row(s, 0);
            Assert.AreEqual(HealthLevel.Warning, row.Level);
            StringAssert.Contains("2", row.Problem);
        }
    }

    public class StageViewportTests
    {
        [Test]
        public void TopLeftRect_BecomesBottomLeftPixelRect()
        {
            var r = StageViewport.ToBottomLeft(160f, 90f, 1600f, 900f, 1080f);
            Assert.AreEqual((160f, 90f, 1600f, 900f), r);
            r = StageViewport.ToBottomLeft(0f, 0f, 100f, 50f, 1000f);
            Assert.AreEqual((0f, 950f, 100f, 50f), r);
        }

        [Test]
        public void Contains_UsesBottomLeftCoordinates()
        {
            var r = (x: 10f, y: 20f, w: 100f, h: 50f);
            Assert.IsTrue(StageViewport.Contains(r, 10f, 20f));
            Assert.IsTrue(StageViewport.Contains(r, 109f, 69f));
            Assert.IsFalse(StageViewport.Contains(r, 111f, 30f));
            Assert.IsFalse(StageViewport.Contains(r, 50f, 71f));
        }
    }
}
