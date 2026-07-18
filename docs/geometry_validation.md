# Passthrough camera geometry — measured values

Measured with `GeometryProbe` on a **Meta Quest 3S over Meta Horizon Link**, Unity 6000.0.73f1,
Meta XR SDK / MRUK 201.0.0, OVRPlugin 1.201.0.

These numbers back the camera model in `Assets/Scripts/EnvLine/CameraGeometry.cs`.

## Camera parameters

Both cameras, requested and delivered at 1280x960:

| | Left | Right |
|---|---|---|
| Sensor resolution | 1280x960 | 1280x960 |
| Delivered resolution | 1280x960 | 1280x960 |
| Focal length (px) | 869.0, 869.0 | 869.0, 869.0 |
| Principal point (px) | 638.0, 479.0 | 638.0, 479.0 |
| Lens offset position (m) | -0.03, -0.02, 0.06 | 0.03, -0.02, 0.06 |
| Lens offset rotation | (0.0979, 0.0027, -0.0046, 0.9952) | (0.0953, -0.0028, -0.0041, 0.9954) |
| Derived crop region | (0, 0, 1280, 960) | (0, 0, 1280, 960) |

Supported resolutions, both cameras: 320x240, 640x480, 800x600, 1280x960.

Requesting the full sensor resolution means the crop covers the whole sensor, so intrinsics pass
through unscaled. Requesting a different aspect ratio (for example 1280x1280) would crop, and the
scaling in `DeriveIntrinsics` would then apply.

Horizontal field of view is `2·atan(640/869) ≈ 72.8°`.

## Projection check

Compares `CameraGeometry.Project` against `PassthroughCameraAccess.WorldToViewportPoint` over
1000 random points 0.5–4 m in front of the camera.

| Half-pixel offset | Result | Max error | Mean error |
|---|---|---|---|
| Not applied | **PASS** | 0.0002 px | 0.0000 px |
| Applied | FAIL | 0.7072 px | 0.7071 px |

**No half-pixel shift.** The failing case is off by 0.7071 px = √2/2, exactly the magnitude of a
(0.5, 0.5) offset, confirming the offset is the only difference between the two and that it does
not belong. The `halfPixelOffset` parameter was removed.

This validates the sensor-crop arithmetic and the Unity↔OpenCV conversion. It does **not** validate
lens distortion or pose/image time alignment: both sides share the same pose and the same pinhole
model, so neither can show up here.

## Stereo

120 frames where both cameras reported a new image.

| | Value | Status |
|---|---|---|
| Capture-time difference | mean 0.34 ms, max 0.56 ms | **not established — see below** |
| Baseline | 6.39 cm, varying by 0.00 mm | valid |

The baseline comes from `GetCameraPose`, which combines headset tracking with the lens offset
and never touches the image path, so it stands. It is constant to the logged precision,
consistent with a rigid rig, and close to human interpupillary distance.

**The capture-time difference is not established.** `Timestamp` is filled by
`CameraAcquireLatestCpuImage` — the same call that returns the colour-bar test pattern over Link.
A timestamp attached to a synthesised frame says nothing about when two real sensors exposed.

Across 30 recorded keyframes the difference ranges 0.337–0.472 ms with 26 distinct values, so it
is jittery rather than a constant, which is what real measurement would look like. But two
synthetic frames generated a few hundred microseconds apart would look the same, and this data
cannot separate the two. **Re-measure on device before relying on it.**

This matters because stereo viability rests on it: if the two cameras are not co-triggered, per
keyframe stereo matching degrades whenever the head or the scene moves.

### Expected depth resolution

With `fx = 869 px` and `B = 6.39 cm`, disparity is `d = fx·B/Z` and depth error is
`ΔZ = Z²·Δd/(fx·B)`. At 0.5 px matching precision:

| Distance | Disparity | Depth error |
|---|---|---|
| 0.5 m | 111 px | 0.2 cm |
| 1 m | 56 px | 0.9 cm |
| 2 m | 28 px | 3.6 cm |
| 3 m | 19 px | 8.1 cm |
| 4 m | 14 px | 14.4 cm |
| 5 m | 11 px | 22.5 cm |

Accurate close in and degrading quadratically with distance — well suited to tabletop work,
weak for room-scale walls.

## Link does not carry camera images

Over Meta Horizon Link the Passthrough Camera API returns **an eight-bar colour test pattern**,
not camera imagery. Verified by inspecting recorded frames: two distinct values per channel,
identical statistics in both eyes, zero SIFT keypoints. The pattern arrives through
`CameraAcquireLatestCpuImage`, so it originates in Meta's runtime rather than in application code.

Meta's Passthrough Camera API sample repository states: "The XR Simulator does not support the
Passthrough Camera API. Testing should be done on a physical device or via Meta Horizon Link
2.1+; agents that try to verify in-editor will silently get nothing."

The "or via Meta Horizon Link 2.1+" part did not hold here: the test pattern was observed with a
current Link installation. Treat the measurement as authoritative and the documentation as
optimistic — images require a device build.

Intrinsics, poses and timestamps **are** real over Link, which is why the measurements above
remain valid — none of them uses pixels. **Anything touching image content requires an on-device
build.**

## Caveats

- Measured over Link, not on device. The capture-time difference is a difference between two
  sensor timestamps, so transport latency should cancel, but this is worth re-checking on device.
- Lens distortion is unmeasured. `CameraIntrinsics` exposes no distortion coefficients, so the
  model assumes a pinhole camera. Detecting distortion requires comparing against image
  measurements rather than the SDK's own projection.
- Pose/image time alignment is likewise unmeasured, for the same reason.

## Environment notes

The scene needs an `OVRCameraRig`; no `OVRPassthroughLayer` is required, since camera access does
not depend on passthrough being rendered. OpenXR must be enabled for **PC/Standalone** in XR
Plug-in Management (Android alone is not enough for Editor play), and Meta Quest Link must be the
active OpenXR runtime on Windows.

Meta's `[BuildingBlock] Passthrough Camera Access` object, if present, already owns the left
camera. Only one component may own a given camera, so `GeometryProbe` adopts an existing one
rather than creating a second.
