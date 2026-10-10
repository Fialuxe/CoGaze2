# CoGaze.Transport.WebRtc — not implemented yet (#20)

Unity WebRTC transport. Worker sends H.264 passthrough video; both sides open one unreliable data
channel (GazeCue) and one reliable channel (ControlMessage), encoded with `CoGaze.Protocol`.
Signaling goes through `server/service.py` (#19).

Implements `ICueSender`, `ICueReceiver`, `IMessageChannel` from `CoGaze.Core`. Its asmdef will
reference only `CoGaze.Core`, `CoGaze.Protocol` and `Unity.WebRTC`, with a `versionDefines` entry on
`com.unity.webrtc` so projects without the package still compile.

Blocked by #18 (TBD-04, how `video_frame_id` travels with frames) and #19 (signaling relay).
