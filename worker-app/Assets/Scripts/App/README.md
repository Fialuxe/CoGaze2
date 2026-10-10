# CoGaze.Worker.App

Bootstrap only. `WorkerBootstrapBehaviour` has two modes:

- **Editor rehearsal** (`editorRehearsal` on, editor only): the cue display with synthetic cues, a
  manual desk plane, main-camera poses, cycling the visualisations every 6 s. Add it to an empty
  scene with a camera and press Play. Logs go to `Application.persistentDataPath/sessions`.
- **Live** (on device always; in the editor with `editorRehearsal` off): `WorkerLive` wires the
  WebRTC link (`signalingUrl`, e.g. `ws://<server-ip>:8765/signal`), passthrough capture with frame
  ids, the cue pipeline on the work surface, the condition/trial/clock responder, WorkerStatus at
  1 Hz and `pose.csv` at 30 Hz. Logs go to `persistentDataPath/sessions/<expert's session name>/`
  once the expert opens a session. Needs `com.unity.webrtc` (otherwise it logs an error and idles).

The work surface is still `ManualWorkSurfaceBehaviour`; the QR surface (#23) replaces it here.
