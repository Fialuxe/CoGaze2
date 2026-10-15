# CoGaze.Expert.View3D

The 3D path (#47): the worker's space as a live mesh the expert looks around in, inside the same
app and the same stage rect as the 2D video. Gaze into the mesh (`View3DProjector`) is M4 and not
built yet.

## Parts

- `Model/` (`CoGaze.Expert.View3D.Model`, pure, tested in `tests/`): `OrbitCamera` (orbit / pan /
  zoom / frame / look-from with easing), `ChunkUpdate` (parses `chunks.Update.encode`),
  `ReconstructionStatus` + `ReconstructionHealth` (checklist row and stage label),
  `StageViewport` (video_rect_px → camera pixel rect).
- `MeshReceiver`: live from `server/service.py` `/expert` (reconnects every 3 s), or replays `.cgzm`
  files. Decodes with Draco and converts with `ToUnityPoint` — Y flip **and X negation** (CLAUDE.md,
  "A left-right flip in the reconstruction").
- `ChunkMeshStore`: one GameObject per chunk, each with a `MeshCollider`.
- `View3DBehaviour`: the camera (pixel rect = stage), input, health ticking. Created by the App
  bootstrap; the UI's `ViewSwitchView` switches it on (top bar 2D / 3D, or `V`).
- `Resources/VertexColorUnlit.shader`: URP. The project's URP asset is `Assets/Settings/ExpertURP.asset`,
  re-created by `CoGaze > Set Up Rendering` if it goes missing.

Controls: right-drag orbit, middle-drag or Shift+right-drag pan, wheel zoom, `F` frame all, `Home`
worker's view (the start view until a worker pose reaches `SetWorkerViewpoint`).

## Seeing it

`config/app.json` → `"reconstruction"`: either `"url": "ws://<gpu-pc>:8765/expert"` (live) or
`"staging_dir": "<folder of .cgzm>"` (replay, relative to the repo root), not both. `"view": "3d"`
starts in 3D; it needs one of the two.

Without a server or recording, write a synthetic workbench:

```
python -m venv .venv
.venv\Scripts\pip install numpy DracoPy
.venv\Scripts\python server\make_synthetic_chunks.py        # -> recordings/synthetic_chunks
```

then set `"staging_dir": "recordings/synthetic_chunks"` and play `Expert.unity`. The red mug must be
on the left and the green box on the right; if they swap, the X correction is broken.

## For M4 (gaze in 3D)

`View3DProjector : IGazeRayProjector` takes `View3DBehaviour.ViewCamera`: screen gaze →
`ViewCamera.ScreenPointToRay` → `Physics.Raycast` against the chunk colliders → hit in Unity's
frame. Sending it to the worker means converting back to the server frame with
`MeshReceiver.ToUnityPoint` (its own inverse), or gaze and mesh disagree. The worker's head pose is
not on the `/expert` wire yet; when it is, feed it to `SetWorkerViewpoint` for `Home`.
