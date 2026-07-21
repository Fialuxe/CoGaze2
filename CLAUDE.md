# CoGaze2 — working notes for Claude

Remote collaboration for hands-on physical work: a **worker** in a Quest 3S, an **expert** at a
PC with a webcam, and a **GPU server** between them. The worker's space is reconstructed in 3D so
the expert can look around it independently, and the expert's gaze is drawn back into the worker's
mixed-reality view. The research question is gaze deixis and joint attention from a plain webcam.

Read `docs/next_steps.md` first — it is the authoritative account of what exists and what does not,
and why each choice was made by measurement. `docs/server_contract.md` fixes the wire boundary;
`docs/geometry_validation.md` holds the camera-model measurements.

**Scope is a research prototype.** Don't build redundancy or extensibility for its own sake; don't
scatter error handling or over-split code; pure functions are for geometry. Don't read CoGaze v1 —
it is god-classes, and only its constant values are worth borrowing. Commit as `fialuxe`, no
Co-Authored-By line.

## Layout

- `server/` — Python reconstruction pipeline (batch scripts + the live service).
- `Assets/Scripts/EnvLine/` — Unity capture (worker) and mesh display (expert).
- `recordings/` — recorded sessions and batch meshes; `session01` is 64 real on-device keyframes.
- `third_party/FoundationStereo/` — the learned stereo matcher (weights untracked).

## The environment line (built, working)

Worker's stereo → depth → fused mesh → expert, in the worker's own coordinate frame so nothing has
to be registered. This half is done; the gaze work on top is not started.

Pipeline, all in `server/`:
`keyframes` (load) → `rectify` → `depth/` (estimator by name) → `fusion` (two-grid GPU TSDF) →
`chunks` (Draco-compressed dirty chunks).

- **Batch:** `run_fusion.py <session>` writes whole meshes; `run_chunks.py <session> --out <dir>`
  writes per-keyframe `.cgzm` chunk updates (the staging data for the expert client).
- **Live:** `service.py` is a WebSocket server — worker connects to `/worker` and streams keyframes,
  expert connects to `/expert` and receives chunk updates. It logs every keyframe in the recording
  format first (recovery, and replay), holds the two grids across frames, and derives bounds from
  the first keyframe of each epoch (a fixed box — a grid that follows the worker is a future step).

Test the live loop without a headset: run `service.py`, then a client that streams a recording into
`/worker` and reads `/expert` (see the throwaway `test_stream.py` pattern used during development).

### Wire formats

- **Keyframe** (worker → server, one binary WS message): `CGZK` header, then recording-format JSON
  (index, epoch, w/h, per-camera intrinsics + `world_T_camera`), then two JPEGs. Encoded by
  `KeyframeUploader.cs` / `KeyframeRecorder.Describe`, decoded by `service.decode_keyframe`.
- **Chunk update** (server → expert): `CGZM` header, then per-chunk keys and Draco payloads —
  `chunks.Update.encode`, decoded by `MeshReceiver.ApplyUpdate`. Confidence rides in the vertex
  colour's alpha channel.

### Unity side

- `KeyframeSource` produces posed stereo keyframes; `KeyframeRecorder` writes them to disk;
  `KeyframeUploader` sends them to `service.py`.
- `MeshReceiver` displays chunks — from the socket when `serverUrl` is set, else replaying `.cgzm`
  files from disk (staging). Both feed the same `ApplyUpdate`.
- Uses `com.unity.cloud.draco` (`Draco.DracoDecoder.DecodeMesh`, returns a `Mesh` from a
  `NativeArray<byte>.ReadOnly`) and a `VertexColorUnlit` shader (double-sided, shows vertex colour).

## Conventions and gotchas

- **Coordinate frame.** Everything crossing the server boundary is OpenCV: right-handed, +X right,
  +Y down, +Z forward, `world_T_camera`, images top-row-first. Unity↔OpenCV is `CameraGeometry.FlipY`
  (negate Y), validated for projection to 0.0002 px, and used both ways.
- **A left-right flip in the reconstruction (confirmed).** The reconstruction comes out mirrored
  left-right; checked against the real room. `MeshReceiver.ToUnityFrame` corrects it by negating X on
  top of the Y flip. It is *not* fixable in `CameraGeometry.FlipY`: that convention is the textbook
  Unity↔OpenCV one and cv2 rectification needs the server frame right-handed, so the flip is inside
  the reconstruction and is undone at display. Root cause not yet pinned (candidates: passthrough
  image handedness, stereo assignment). **Whatever projects gaze into this geometry must apply the
  same X negation, or gaze and mesh will disagree** — revisit this when gaze work starts. A
  reprojection test looked faithful only because a mirrored mesh viewed through its own mirrored pose
  reproduces the real image; it cannot detect a self-consistent mirror.
- **Link returns colour bars, not camera images.** Anything touching pixels needs an on-device
  build. Intrinsics, poses and timestamps over Link are real.
- **Depth estimators.** `estimator("name")` picks one: `foundation` (FoundationStereo, ~4 s/frame,
  the accuracy reference), `fast` (Fast-FoundationStereo 20-26-39, **2.6 Hz full-res / 9.6 Hz at
  --scale 0.5**, same 91% coverage — use this for live), `sgbm` (baseline, holes on textureless).
  Live: `python service.py --estimator fast [--scale 0.5]`. `--scale`/`--iters` downscale the input;
  resolution is the big speed lever. **Windows/Triton:** Fast-FS's build-volume kernels are
  torch.compile'd and need Triton (absent on Windows), so `fast_foundation_stereo` disables dynamo
  to run them eagerly — that is why full-res is 2.6 Hz not the paper's ~20 FPS; a Linux/Triton or
  TensorRT box would be much faster still. Its checkpoint is a serialized model object, not a state
  dict.
- **Labels break under Draco.** Draco reorders vertices, so the label byte-array carried alongside
  the mesh misaligns once semantics are enabled. Fix: pass labels through
  `DracoPy.encode(generic_attributes=...)` so they ride with the vertices. Harmless now (labels are
  zero until semantics are wired in).
- **Hands are masked out**, not meshed; if hands are ever shown, use Quest hand-tracking, not the
  reconstruction (moving hands smear the TSDF).
- **JPEG orientation** from `ImageConversion.EncodeArrayToJPG` is assumed to keep top-first rows;
  verify on device that the live reconstruction is not vertically flipped versus a recording.

## Reasoning help

An `advisor` tool escalates to a stronger reviewer that sees the whole transcript — call it before
committing to an approach and before declaring done. There is a `codegraph` index for the codebase.
