# CoGaze.Worker.Surface

Work-surface alignment (WRK-F-002..006, VT-09), implementing `IWorkSurface`.

Exists: `ManualWorkSurfaceBehaviour` — the plane is a transform; used in the editor.

To build (#23): `QrWorkSurfaceBehaviour` — detect the desk QR with MRUK QR tracking (experimental
feature, enable in both app and headset), derive the plane, fix it with a spatial anchor, stop QR
tracking; experimenter nudge with offsets written to `alignment.json`; optional known-point markers
for measuring the alignment error. Reports `NotAligned` / `Aligned` / `TrackingLost`.
