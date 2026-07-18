# Next steps

## What CoGaze2 is

A remote collaboration system for hands-on physical work. Three participants in the loop:

- a **worker**, wearing a Quest 3S, doing something with their hands — repairing a car,
  assembling a puzzle, training on bomb disposal;
- an **expert**, at a PC with a webcam, who knows how to do it and is somewhere else;
- a **GPU server** between them.

The worker's space is reconstructed in 3D so the expert can look around it from any
viewpoint, independently of where the worker happens to be facing. The expert's gaze, head
pose and a confidence value are shared every frame and drawn into the worker's mixed
reality view, so the worker can see what the expert is looking at. The expert's annotations
appear in the same space.

The research question is about **deixis and joint attention**: pointing at things with
gaze, and both people coming to attend to the same thing. Specifically, whether a webcam —
noisy, uncalibrated, no infrared — can carry gaze well enough to point with, and how to
present it honestly when it cannot. Gaze that is confidently wrong is worse than gaze that
admits uncertainty, so the presentation changes with confidence rather than always showing
a crisp ray.

### Why the reconstruction

The predecessor system used a mesh built in advance and aligned to the room with QR
markers, which meant a scan, printed markers and a calibration ritual for every new space.
Reconstructing at runtime removes all three. Because the headset's own tracking supplies
the camera pose, the geometry lands in the worker's coordinate frame directly — the same
frame the expert's gaze has to be projected into — so there is no registration step to get
wrong. **That removal is the concrete contribution of this half of the system.**

### Where the work is now

The environment line — worker's space to a mesh the expert can look at — is measured and
working end to end on recorded data. Everything else, including all of the gaze work that
is the actual contribution, is not started.

---

Measurements below are from 64 keyframes recorded on a Quest 3S; see
`geometry_validation.md` for the geometry and `server_contract.md` for what crosses the
boundary.

## Working

| | Measured |
|---|---|
| Capture on device | stereo keyframes, poses, intrinsics, timestamps |
| Camera model | agrees with the SDK to 0.0002 px |
| Depth (FoundationStereo) | 91% coverage, 4.16 s/frame, 7.2 GB |
| Compression | JPEG q95: 8 Mbps up, depth moves 0.55 mm against a 4.5 mm noise floor |
| Semantics | 68 ms/frame, 28% of vertices named |
| Fusion | GPU TSDF, 4 mm near / 30 mm far |
| Chunked meshing | 0.3 ms/chunk, 0.13 s to remesh what two keyframes touched |
| Chunk transport | 8.3 KB/chunk Draco + labels, 2.4 Mbps |

Everything above runs as batch scripts over a recorded session.

## Streaming — the next piece

Turning the batch pipeline into a service. The computation does not change; what is
missing is the loop around it and the two sockets.

### `server/service.py`

- WebSocket server accepting keyframes
- **Append every keyframe to a log before doing anything else.** The TSDF lives only in
  GPU memory, so without this a crash costs the worker the whole session. At roughly
  0.5 MB a keyframe the log is cheap, and it doubles as the record that lets a session be
  reprocessed with a different estimator — which is how every comparison so far was made.
- Hold the two `Grid` instances across frames rather than rebuilding
- Per keyframe: depth → semantics → integrate → `take_dirty()` → `chunks.build()` → push
- On epoch change: clear the grids, emit `reset`
- Do **not** replay old keyframes to re-mesh. Geometry the worker has moved away from
  stays as it was; the log is for recovery, not for continuous rebuilding.

### `KeyframeUploader.cs` — worker side

- Replaces `KeyframeRecorder`'s disk write with a WebSocket send
- Encode JPEG q95 before sending; the recorder currently writes raw
- `KeyframeSource` needs no change — it already emits an event

### `MeshReceiver.cs` — expert side

Does not exist yet.

- Dictionary of chunk key to GameObject
- Decode Draco via `com.unity.cloud.draco` into a `Mesh`
- Apply the Y flip back into Unity's frame — the same operation used on the way out
- Replace or remove per chunk key; clear everything on `reset`

### Staging

Have the server write chunk files to a directory and the expert client read them, before
introducing the socket. That isolates whether Unity renders our chunks correctly — the
coordinate flip especially — from whether the transport works. Swap disk for WebSocket
once the geometry appears in the right place.

## Also outstanding

- **Time-based keyframe trigger.** Keyframes fire on pose change alone, so a worker
  holding still while moving a part produces nothing and the server never sees the change.
  Needed before "the near field updates every second" can hold.
- **Fast-FoundationStereo.** 4.16 s/frame is fine for an initial scan and far too slow to
  live-update. One line in `depth/__init__.py` once the weights are in.
- **Mask people out of the geometry.** `person` already comes back from segmentation, so
  dropping those pixels before integration is a few lines. Covers most of what hand and
  body masking was for.
- **Haptic feedback on the controller** when frames are being sent, so the worker can tell
  the system is alive without taking the headset off.
- **Long sessions.** The near grid should follow the worker, with geometry left behind
  settling into the static far grid. Periodic TSDF snapshots and log truncation, so
  recovery does not mean replaying an hour.

## The rest of the system

Everything above is the environment line — reconstructing the worker's space so the expert
has something to look at. None of it is the contribution. The contribution is what happens
on top, and none of it is started.

### State sharing

The expert's gaze, head pose and a confidence value, shared every frame and projected into
the worker's space.

- **Gaze estimation from a webcam.** Two prior repositories exist outside this project;
  the newer one supports webcam and IR, and deliberately leaves head-pose compensation as
  future work — a constraint if the expert moves while looking.
- **Confidence without infrared.** Needed to drive the presentation below. Infrared serves
  as the comparison condition, not the source.
- **Calibration.** Nine points over about thirty seconds once, plus implicit correction
  from where the expert clicks, to absorb drift.
- **Head pose**, both as a variable in the error model and as the fallback pointer when
  gaze confidence collapses.

### Presenting gaze in the worker's space

Presentation follows confidence rather than showing one thing always:

| Confidence | Shown |
|---|---|
| High | a ray with no tip sphere, or a translucent circle whose radius follows the error |
| Medium | a frustum whose angle widens with uncertainty |
| Low | gaze hidden; fall back to spoken confirmation |

Screen gaze becomes a 3D point by casting from the expert's virtual camera into the
reconstruction. Error grows with distance and is amplified by a wide virtual field of view.

- **Annotations** drawn by the expert, projected into the worker's MR space.
- **Ghost presentation** for assembly: the target pose shown translucent, so gaze carries
  position while the ghost carries orientation and fewer rotations need saying aloud.

### Open question the reconstruction feeds into

Whether the mesh is the surface gaze rays terminate on was deliberately deferred, and now
has evidence on both sides. The mesh is metric and in the worker's own frame, which is what
a referent needs. But it is 91% complete rather than whole, it lags a moving part by
seconds, and it carries per-vertex confidence that a pointing interface could either use or
ignore. Whether the expert points at geometry, at an object identified by label, or at the
live video, is now a question that can be settled with measurements rather than argued.

Showing the expert what has *not* been observed belongs here too: an unobserved region is
something to ask the worker to look at, which is joint attention being constructed rather
than a gap being hidden.

### Devices

Quest 3 / 3S is the current target. The predecessor system ran on HoloLens 2. Smart glasses
were raised as an option; the wire protocol is device-agnostic by construction, since the
server accepts posed stereo in OpenCV convention and knows nothing about what produced it.

## Not being done, and why

**Inferring unobserved geometry.** Nothing published fits: methods run at 2–5 cm where
4 mm is needed, are trained on room and furniture categories rather than tools and parts,
and do not generalise to them. SCFusion is the right shape — 64³ frustum submaps at
123 ms would drop into the chunk pipeline nearly unchanged — but at 5 cm with an 11-class
furniture prior it would draw a table-shaped blob behind a bottle. Nobody completes
unobserved geometry live for a remote viewer; every live telepresence system streams
measured geometry only.

The alternative the literature supports better is to show the expert what has *not* been
seen and let them ask the worker to look — which is the deixis and joint attention this
project is about anyway.
