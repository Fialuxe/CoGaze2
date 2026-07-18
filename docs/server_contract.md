# Reconstruction server contract

What the server accepts and what it emits. Fixing this lets mesh completion and mesh
delivery proceed independently, since both sides only depend on the shapes below.

Everything crossing this boundary uses the **OpenCV convention**: right-handed, +X right,
+Y down, +Z forward; poses are `world_T_camera`; images have their top row first. The
headset converts out of Unity's frame before sending, and the expert client converts back
with the same operation, which is its own inverse.

## Input — keyframe, worker to server

One stereo pair with everything needed to reconstruct from it.

| Field | Type | Notes |
|---|---|---|
| `index` | uint32 | monotonic within a session |
| `epoch` | uint32 | increments when tracking jumps; see below |
| `timestamp_ns` | int64 | capture time, shared by both cameras |
| `width`, `height` | uint16 | per image |
| `left`, `right` | camera | `fx, fy, cx, cy` and `world_T_camera`; the two differ |
| `left_rgb`, `right_rgb` | bytes | tightly packed RGB, top row first |

Intrinsics are per-camera and are not interchangeable — the two cameras are individually
calibrated and differ by around 6 px in focal length.

**Epoch.** Tracking loss and user recentring both move the world origin. Everything
reconstructed before the jump is expressed in a frame that no longer exists, so a change
of epoch invalidates the whole map. The server clears its volumes and emits a `reset`.

**Triggers.** The worker sends on pose change (15 cm or 15°) *and* on a timer. The timer
is not redundant: a worker who holds still while moving a part produces no pose change,
so a pose-only rule would never report the part moving.

## Output — mesh updates, server to expert

The map is divided into fixed spatial chunks and only changed chunks are sent. A chunk is
identified by its integer block coordinate, so the client can hold a dictionary from key
to renderable and replace entries as they arrive.

| Message | Fields | Meaning |
|---|---|---|
| `reset` | `epoch` | discard everything; a new frame of reference has begun |
| `chunk` | `epoch`, `key`, `lod`, `geometry` | replace this chunk |
| `remove` | `epoch`, `key` | this chunk no longer has a surface |

`key` is `(x, y, z)` block indices at a fixed block size. `lod` distinguishes the near
grid (4 mm voxels, 0.3–1.5 m) from the far grid (30 mm, 1.5–7 m); the two cover different
distances and are drawn together rather than merged.

`geometry` is a glTF-Binary buffer with positions, triangles, normals, vertex colours, and
one extra per-vertex attribute:

### Provenance

Every vertex carries a **confidence in [0, 1]** reflecting how well that surface was
measured — how many keyframes contributed and at what incidence angle. A surface glimpsed
once edge-on and one seen twenty times face-on are both drawn, and the expert should be
able to tell them apart before pointing at either.

**Nothing unobserved is invented.** Surfaces no camera reached are absent. A hole is
honest about what is unknown; a confident wrong surface is not.

Geometry that closes gaps between measured surfaces — smoothing, small-hole repair,
interpolation across dropouts — is fair game and carries reduced confidence. Extending a
surface into space no camera reached is not, until a method exists that produces
plausible shape there rather than uniform thickness.

## What the server does not do

- **It does not estimate pose.** Poses arrive from headset tracking and are used as given,
  which is what keeps the reconstruction in the worker's own frame and removes any
  registration step.
- **It does not reconstruct moving objects as geometry.** Hands and parts in motion are
  carried by the live video channel. The mesh represents the scene as it settles, with old
  observations decaying so a moved part stops being supported where it used to be.
