using NUnit.Framework;
using UnityEngine;

namespace CoGaze2.EnvLine.Tests
{
    /// <summary>
    /// Tests for CameraGeometry. These run without a headset and without the Meta XR packages,
    /// since CameraGeometry depends on neither.
    ///
    /// They cover the sensor-crop arithmetic, the Y-flip's self-inverse and rotation-preserving
    /// properties, and image orientation. They cannot confirm that these conventions match the
    /// SDK's actual behaviour — <c>GeometryProbe</c> checks that against the headset.
    /// </summary>
    public class CameraGeometryTests
    {
        // Quest 3 passthrough: 1280x960 sensor, 640x480 requested (same 4:3 aspect).
        static readonly Vector2Int Sensor = new Vector2Int(1280, 960);
        static readonly Vector2Int Delivered = new Vector2Int(640, 480);
        static readonly Vector2 Focal = new Vector2(500f, 500f);
        static readonly Vector2 Principal = new Vector2(640f, 480f); // sensor centre

        static CameraGeometry.Intrinsics MakeK() =>
            CameraGeometry.DeriveIntrinsics(Focal, Principal, Sensor, Delivered, halfPixelOffset: false);

        // ---- crop region -------------------------------------------------------------------

        [Test]
        public void CropRegion_MatchingAspect_CoversWholeSensor()
        {
            Rect crop = CameraGeometry.SensorCropRegion(Sensor, Delivered);
            Assert.AreEqual(0f, crop.x, 1e-4f);
            Assert.AreEqual(0f, crop.y, 1e-4f);
            Assert.AreEqual(1280f, crop.width, 1e-4f);
            Assert.AreEqual(960f, crop.height, 1e-4f);
        }

        [Test]
        public void CropRegion_SquareRequest_CropsHorizontallyAndStaysCentred()
        {
            // 1280x1280 from a 4:3 sensor must crop the sides, not the top/bottom.
            Rect crop = CameraGeometry.SensorCropRegion(Sensor, new Vector2Int(1280, 1280));
            Assert.AreEqual(160f, crop.x, 1e-4f, "crop should be horizontally centred");
            Assert.AreEqual(0f, crop.y, 1e-4f);
            Assert.AreEqual(960f, crop.width, 1e-4f);
            Assert.AreEqual(960f, crop.height, 1e-4f);
            // centred means equal margins on both sides
            Assert.AreEqual(Sensor.x - crop.xMax, crop.x, 1e-4f);
        }

        // ---- intrinsics --------------------------------------------------------------------

        [Test]
        public void DeriveIntrinsics_MatchingAspect_ScalesFocalAndCentresPrincipalPoint()
        {
            var k = MakeK();
            Assert.AreEqual(250f, k.fx, 1e-3f, "fx must scale by delivered/sensor width");
            Assert.AreEqual(250f, k.fy, 1e-3f);
            Assert.AreEqual(320f, k.cx, 1e-3f, "a centred sensor principal point stays centred");
            Assert.AreEqual(240f, k.cy, 1e-3f);
        }

        [Test]
        public void DeriveIntrinsics_HalfPixelOffset_ShiftsOnlyThePrincipalPoint()
        {
            var without = MakeK();
            var with = CameraGeometry.DeriveIntrinsics(Focal, Principal, Sensor, Delivered, true);
            Assert.AreEqual(without.fx, with.fx, 1e-6f, "focal length must not depend on the offset");
            Assert.AreEqual(without.cx - 0.5f, with.cx, 1e-6f);
            Assert.AreEqual(without.cy - 0.5f, with.cy, 1e-6f);
        }

        // ---- Unity <-> OpenCV conversion ---------------------------------------------------

        [Test]
        public void FlipY_IsInvolutive()
        {
            var pose = new Pose(new Vector3(1.5f, -2.25f, 3f),
                                Quaternion.Euler(23f, -47f, 61f));
            Pose round = CameraGeometry.FlipY(CameraGeometry.FlipY(pose));

            Assert.AreEqual(pose.position.x, round.position.x, 1e-5f);
            Assert.AreEqual(pose.position.y, round.position.y, 1e-5f);
            Assert.AreEqual(pose.position.z, round.position.z, 1e-5f);
            Assert.That(Quaternion.Angle(pose.rotation, round.rotation), Is.LessThan(1e-3f));
        }

        [Test]
        public void FlipY_ConjugatesRotationRatherThanMirroringIt()
        {
            // Defining property of R' = S R S : R'(S v) == S (R v).
            // A sign error that mirrors instead of conjugating fails this.
            var rot = Quaternion.Euler(17f, 39f, -73f);
            var v = new Vector3(0.3f, -1.2f, 2.4f);

            Vector3 lhs = CameraGeometry.FlipY(new Pose(Vector3.zero, rot)).rotation
                          * CameraGeometry.FlipY(v);
            Vector3 rhs = CameraGeometry.FlipY(rot * v);

            Assert.That(Vector3.Distance(lhs, rhs), Is.LessThan(1e-4f));
        }

        // ---- projection --------------------------------------------------------------------

        [Test]
        public void Project_PointOnOpticalAxis_LandsOnPrincipalPoint()
        {
            var k = MakeK();
            bool ok = CameraGeometry.Project(new Vector3(0f, 0f, 5f), Pose.identity, k,
                                             out Vector2 px, out float depth);
            Assert.IsTrue(ok);
            Assert.AreEqual(5f, depth, 1e-4f);
            Assert.AreEqual(k.cx, px.x, 1e-3f);
            Assert.AreEqual(k.cy, px.y, 1e-3f);
        }

        [Test]
        public void Project_PointBehindCamera_IsRejected()
        {
            var k = MakeK();
            Assert.IsFalse(CameraGeometry.Project(new Vector3(0f, 0f, -1f), Pose.identity, k,
                                                  out _, out _));
        }

        [Test]
        public void Project_UnityRightAndUp_MapToImageRightAndUp()
        {
            // A handedness or cy-flip sign error inverts one of these axes while leaving every
            // other test passing, so both directions are asserted explicitly.
            var k = MakeK();

            // Camera at the Unity origin, identity rotation: +X right, +Y up, +Z forward.
            Pose camCv = CameraGeometry.FlipY(Pose.identity);

            Vector3 rightCv = CameraGeometry.FlipY(new Vector3(1f, 0f, 5f)); // 1 m to the right
            Vector3 upCv = CameraGeometry.FlipY(new Vector3(0f, 1f, 5f)); // 1 m up

            Assert.IsTrue(CameraGeometry.Project(rightCv, camCv, k, out Vector2 pxRight, out _));
            Assert.IsTrue(CameraGeometry.Project(upCv, camCv, k, out Vector2 pxUp, out _));

            Assert.Greater(pxRight.x, k.cx, "a point to the right must land right of centre (+u)");
            Assert.AreEqual(k.cy, pxRight.y, 1e-3f, "a purely lateral point must not shift vertically");

            Assert.Less(pxUp.y, k.cy, "a point above must land above centre, i.e. SMALLER v");
            Assert.AreEqual(k.cx, pxUp.x, 1e-3f, "a purely vertical point must not shift laterally");
        }

        [Test]
        public void PixelToViewport_RoundTripsThroughProjection()
        {
            var k = MakeK();
            Vector3 worldCv = CameraGeometry.FlipY(new Vector3(0.4f, 0.25f, 3f));
            Pose camCv = CameraGeometry.FlipY(Pose.identity);

            Assert.IsTrue(CameraGeometry.Project(worldCv, camCv, k, out Vector2 px, out _));
            Vector2 vp = CameraGeometry.PixelToViewport(px, k);

            Assert.That(vp.x, Is.InRange(0f, 1f));
            Assert.That(vp.y, Is.InRange(0f, 1f));
            // MRUK viewport is bottom-left origin, so a point above centre is above 0.5 there.
            Assert.Greater(vp.y, 0.5f, "up in the world must be up in MRUK viewport space");
        }

        [Test]
        public void Project_TranslatedCamera_IsRelativeToTheCamera()
        {
            var k = MakeK();
            // Camera 1 m to the right in Unity; a world point 1 m to the right sits on its axis.
            Pose camCv = CameraGeometry.FlipY(new Pose(new Vector3(1f, 0f, 0f), Quaternion.identity));
            Vector3 worldCv = CameraGeometry.FlipY(new Vector3(1f, 0f, 4f));

            Assert.IsTrue(CameraGeometry.Project(worldCv, camCv, k, out Vector2 px, out float depth));
            Assert.AreEqual(4f, depth, 1e-4f);
            Assert.AreEqual(k.cx, px.x, 1e-3f);
            Assert.AreEqual(k.cy, px.y, 1e-3f);
        }
    }
}
