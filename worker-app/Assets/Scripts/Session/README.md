# CoGaze.Worker.Session (#26)

Pure parts of the live worker, driven by `App/WorkerLive`:

- `ControlResponder` — applies Condition and echoes its id (ConditionAck), applies Trial, answers
  ClockPing with the worker clock, passes the Session folder name on; logs to `events.csv`.
- `StatusReporter` — WorkerStatus at 1 Hz with the measured frame rate, alignment, recording.
- `PoseLogger` — `pose.csv` at 30 Hz; untracked hands leave empty fields.
- `ForwardingLogSink` — drops (and counts) rows until the expert's Session message names the folder.

Clock pongs are stamped when the control queue is drained (once per frame), so a ping can wait up
to one frame on the worker; the expert's minimum-RTT filter is what keeps that out of the offset.
