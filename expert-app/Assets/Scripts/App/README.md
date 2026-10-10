# CoGaze.Expert.App

Bootstrap only: reads `config/`, picks real or fake parts, connects them, and ticks them each frame.
It is the one assembly allowed to reference every other. No experiment logic lives here.

Scene: `Assets/Scenes/Expert.unity` holds a single GameObject with `ExpertBootstrapBehaviour`;
the bootstrap creates the UI itself.

Per frame: drain the worker channel (TrialRunner, WorkerStatusMonitor) → `GazePipeline.Tick` →
health reporters → recording indicator → rehearsal preview.
