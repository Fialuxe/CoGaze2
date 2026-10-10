# CoGaze.Worker.Capture (#21)

Passthrough frames for the expert's video, each with a `video_frame_id`, capture time, pose and
intrinsics written to `frames.csv` and added to `IFramePoseHistory` (WRK-F-010..013).

- `FrameRecorder` (pure, `CoGaze.Worker.Capture`): ids from 1 (0 = "no frame" on the wire), history,
  `frames.csv` rows in `LogSchema.Frames` order. Tested in `Tests/EditMode/SessionTests.cs`.
- `Passthrough/PassthroughCaptureBehaviour` (`CoGaze.Worker.Capture.Passthrough`, references MRUK):
  drives the left `PassthroughCameraAccess` like `EnvLine/KeyframeSource`, at end of frame, and hands
  out (texture, `CameraFrame`). Pose = `GetCameraPose()` (Unity world, at the image timestamp);
  intrinsics = `CameraGeometry.DeriveIntrinsics` (OpenCV, delivered image). Capture time = this app's
  clock minus the image's age from `PassthroughCameraAccess.Timestamp`.

The App puts each frame into the video with `StripFrameTagWriter` (TBD-04) and records it here.

**Needs an on-device build** (Link gives colour bars, not camera pixels): image content, that the
image and the recorded pose belong together (`GeometryProbe`), the camera frame rate actually
achieved, and the camera permission prompt on first run.
