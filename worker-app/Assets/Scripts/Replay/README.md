# CoGaze.Worker.Replay

Recorded or synthetic gaze as an `ICueReceiver`, displayed through the live path as source
`replay` (WRK-F-040, 041).

Exists: `SyntheticCueReceiver` — an editor stand-in for the expert.

To build (#35): read a recorded series (plane hit + sigma) and synthesize one from the task's
correct target plus a measured error series.
