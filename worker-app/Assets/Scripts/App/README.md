# CoGaze.Worker.App

Bootstrap only. `WorkerBootstrapBehaviour` currently runs the editor rehearsal of the cue display
(synthetic cues, manual desk plane, main-camera poses, visualisation cycling every 6 s). Add it to an
empty scene with a camera and press Play. Logs go to `Application.persistentDataPath/sessions`.

Live wiring lands here as the modules arrive: transport (#20), capture (#21), QR surface (#23),
WorkerStatus at 1 Hz, `pose.csv` at 30 Hz, clock-ping responder, condition echo (#26).
