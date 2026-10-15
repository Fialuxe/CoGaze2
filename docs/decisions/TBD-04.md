# TBD-04 — how `video_frame_id` travels with each video frame

**Status: provisional** (decided on code reading and synthetic tests; the on-device numbers below are
still owed). Issue #18. Requirements: WRK-F-011..013, EXP-F-023.

## The question

The expert's gaze is sent as (u, v) on *the frame that was on screen when the gaze was sampled*;
the worker turns it into a ray with *that frame's* camera pose (SRS 3.3). So the expert must know,
for every frame it actually presents, which worker frame it is.

## What Unity WebRTC (com.unity.webrtc 3.0.0) exposes

Read from the package source:

- **Receive side.** A remote `VideoStreamTrack` hands C# one `Texture` (via `OnVideoReceived`, fired
  on the first frame and on resize). Decoded frames are uploaded into that texture on the render
  thread by a native sink. No per-frame callback, no RTP timestamp, no frame id reaches C#.
- **Send side.** `VideoStreamTrack(Texture)` copies the source texture every Unity frame inside
  `WebRTC.Update()`; RTP timestamps are assigned natively. C# cannot tell which copy became which
  encoded frame, nor which were dropped by the encoder.
- **Encoded transforms.** `RTCRtpScriptTransform` gives C# the *encoded* frames with their RTP
  timestamp and `GetMetadata().frameId`, and allows rewriting their bytes. But the receiver only sees
  frames *before* the jitter buffer and decoder; which one is in the texture when Unity draws it is
  still unknown. The metadata `frameId` is libwebrtc's own counter, not ours.

## Options

| Option | Exact per displayed frame? | Cost / risk |
|---|---|---|
| (a) **Pixel strip**: draw the id as black/white cells into the picture; the expert reads them back from the texture it is showing | **Yes** — the id is in the same pixels the expert looks at | 16 px of extra rows; a tiny GPU readback per displayed frame; must survive H.264 |
| (b) Send (frame_id, capture time) on the data channel, match by RTP timestamp | No — C# has neither the RTP timestamp of the displayed texture nor of the sent copy | Would need native changes |
| (b′) Encoded-transform trailer: append the id to each encoded frame, strip it on receipt | No — gives the id of frames *entering* the decoder, not the one on screen; jitter-buffer delay varies | Android transform path is marked fragile in the package (`NoOptimization` workaround) |
| (c) WebRTC frame metadata | No — not exposed for decoded frames; `frameId` is internal | — |

## Decision

**(a) Pixel strip.** It is the only option that ties the id to the exact pixels on the expert's
screen, and it needs nothing from WebRTC beyond "send this texture, show me that texture".

Implementation (`packages/com.cogaze.shared/Runtime/Transport.WebRtc/`):

- `Pure/FrameIdStrip.cs` — the format. A strip `FrameIdStrip.HeightPx` = 16 px high (one macroblock
  row) **below** the camera image, so no image pixel is lost and (u, v) of the image are unchanged.
  44 cells across: 4 marker cells (white, black, white, black) that set the threshold, 32 id bits
  MSB first, CRC-8 of the id. At 1280 px a cell is 29 px wide. The reader averages only the centre
  of each cell and the middle rows. Tested with a 5-px blur, contrast squeezed to 60..190 and ±30
  noise; a single wrong bit and a mirrored strip are rejected.
- `StripFrameTagWriter` (worker) composes camera image (top) + strip (bottom) into the texture the
  video track sends, at end of frame when the camera texture holds the image its pose belongs to.
  Id 0 means "no camera frame yet".
- `StripFrameTagReader` (expert) issues an `AsyncGPUReadback` of the strip rows of the received
  texture once per displayed frame, timestamped when requested (that is the moment the frame is on
  screen), and yields (t_shown, id) once per distinct id to `DisplayedFrameLog.Record`.
- The seam: `IFrameTagWriter` / `IFrameTagReader`. Changing the mechanism means two new
  implementations and nothing else; the strip format is versionless only because it is provisional.

Failures are visible, not silent: the expert checklist row "Worker video" turns red when ids are
unreadable and amber when more than 10 % fail; the worker logs `pose_fallback` in `cue.csv` whenever
a cue's frame id is not in its pose history.

## Must be measured on device before this is final

1. **Orientation.** The strip must arrive at the bottom of the received texture (texture rows
   0..15). If "Worker video" is red with "ids unreadable" while the picture looks right, the
   send/receive vertical flips do not cancel on Quest → expert; check whether the strip is drawn at
   the top of the expert's video (it is hidden by the crop, so temporarily show the full texture).
2. **Readability under real H.264.** Run 10 min with head motion at the chosen bitrate (8 Mbit/s cap,
   TBD-10). Record `Decoded` / `Failed` from the reader (the checklist row shows %). Target: ≥ 99 %
   of displayed frames decoded. Also try 2 Mbit/s to know the margin.
3. **Mismatch rate (the number #18 asks for).** Point the Quest camera at a monitor showing a running
   millisecond clock, stream, and on the expert compare the clock visible in each displayed frame
   with the capture time `frames.csv` gives for the decoded id. Any frame whose id maps to a capture
   time ≠ its visible clock (beyond one camera period) is a mismatch. Target: 0.
4. **Image/pose agreement on the worker.** The writer runs at end of frame like `KeyframeSource`; check
   with `GeometryProbe` that the pose recorded for an id projects onto that id's image (the
   PassthroughCameraAccess docs warn that a blit earlier in the frame picks the previous image).
5. **Cost.** Worker frame time with the extra blit (WRK-N-001, ≥ 72 fps); expert readback latency
   (should be 1–2 frames; the id is stamped with the request time, so latency does not bias it).

If (2) or (3) fails, the fallback is a larger strip (32 px, fewer bits) before abandoning pixels.
