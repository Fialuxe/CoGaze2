# CoGaze.Expert.View3D

The 3D path (M4, #47): the worker's space as a live mesh the expert can look around, and the
expert's screen gaze cast into it.

Exists: `MeshReceiver` (moved from EnvLine) — chunked Draco meshes from `server/service.py`, or
`.cgzm` files from `recordings/session01_chunks` when `serverUrl` is empty. `VertexColorUnlit`
is a URP shader: the scene needs a URP pipeline asset assigned before it renders.

To build: `View3DProjector : IGazeRayProjector` — cast from the expert's virtual camera into the
mesh and return a world-frame ray. **It must undo the same X negation `MeshReceiver.ToUnityFrame`
applies**, or gaze and mesh disagree (see CLAUDE.md, "A left-right flip in the reconstruction").
