# CoGaze.Transport.WebRtc (#20)

The live expert-worker link (SRS 3.3) on `com.unity.webrtc` 3.0.0 (the first release for Unity 6000.0;
Android ARM64 included). `WebRtcLinkBehaviour` implements `ICueSender`, `ICueReceiver` and
`IMessageChannel`:

- one peer connection; the **worker offers**, the expert answers;
- data channel `cue` (id 0, unordered, no retransmits) for `GazeCue`, `control` (id 1, ordered,
  reliable) for `ControlMessage`, both pre-negotiated and encoded with `CoGaze.Protocol.MessageCodec`;
- the worker's video as a send-only H.264 track (8 Mbit/s, 30 fps caps, TBD-10); the expert exposes
  it as `ReceivedVideo`;
- signaling through `server/signaling.py` (`/signal/expert`, `/signal/worker`, JSON in
  `Pure/SignalMessage.cs`); `app.json` `network.signaling_url` is the base, the role is appended.

**Staying up (EXP-N-002).** The signaling socket reconnects with backoff on its own. A peer
connection that fails, stays disconnected 5 s or does not finish negotiating in 15 s is torn down and
the worker re-offers. A relay outage alone does not touch a working connection. `LinkMonitor` shows
the state on the checklist and writes `link_down` / `link_up gap_ms=…` to `events.csv`. Control
messages that change state (Session, Condition, Ack, Trial, WorkerEvent) are held while the link is
down and delivered in order on reconnect; ClockPing and WorkerStatus are dropped (`ControlOutbox`).

**video_frame_id (TBD-04, provisional).** Carried in pixels: a 16-px strip of black/white cells under
the camera image (`Pure/FrameIdStrip.cs`). Worker: `StripFrameTagWriter` composes camera image + strip
into the streamed texture. Expert: `StripFrameTagReader` reads the strip back from the received
texture once per displayed frame and yields (time shown, id) for `DisplayedFrameLog`. Both sit behind
`IFrameTagWriter` / `IFrameTagReader`, the seam to swap if the decision changes. Reasoning and the
on-device measurements still owed: `docs/decisions/TBD-04.md`.

**Layout.** `Pure/` is engine-free and compiled by `tests/CoGaze.Pure` (tests in
`Tests/Pure/WebRtcPureTests.cs`); everything that touches `Unity.WebRTC` is inside
`#if COGAZE_WEBRTC`, defined by `versionDefines` when `com.unity.webrtc` ≥ 3.0.0 is installed, so a
project without the package still compiles (the apps then show the transport as unavailable).
References: `CoGaze.Core`, `CoGaze.Protocol`, `Unity.WebRTC`.
