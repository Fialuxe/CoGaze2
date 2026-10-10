# CoGaze.Worker.Capture (#21)

Passthrough frames for the expert's video, each with a `video_frame_id`, capture time, pose and
intrinsics written to `frames.csv` and added to `IFramePoseHistory` (WRK-F-010..013).

Reuse `CoGaze.Geometry.CameraGeometry` for intrinsics (measured to 0.0002 px against the SDK) and
`EnvLine/KeyframeSource` for how `PassthroughCameraAccess` is driven. Camera images do not come over
Link — anything touching pixels needs an on-device build.

Blocked by #18 (TBD-04: how the id travels with the video) and #20 (WebRTC transport).
