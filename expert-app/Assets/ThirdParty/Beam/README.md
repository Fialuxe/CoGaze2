# Beam Eye Tracker SDK (not committed)

The SDK licence forbids putting it in a public repository, so this folder holds only this README
and the assembly definition. To enable Beam:

1. Download the SDK zip from https://beam.eyeware.tech/developers (2.x).
2. Copy into this folder:
   - `csharp/Eyeware/BeamEyeTracker.cs`
   - `bin/win64/beam_eye_tracker_client.dll` → `Plugins/x86_64/` (set its platform to Windows x64 in the Inspector)
3. Player Settings → Other Settings → Scripting Define Symbols: add `COGAZE_BEAM`.
4. In `config/app.json` set `"beam": { "enabled": true }`, and set `px_scale` to the Windows display
   scaling (1.25 for 125 %) — Beam reports logical pixels, CoGaze works in physical ones.

Without these steps the project still opens and rehearsal runs; `CoGaze.Expert.Sources.Beam` is
simply not compiled.

Licence conditions that the app enforces or the procedure must cover (BET-SDK-License-1.0):
explicit participant consent before tracking (COM-N-010), a visible indicator while tracking
(the red "Eye tracking active" mark, EXP-F-052), and telling participants it is not a medical device.

The wrapper API (`API`, `ViewportGeometry`, `GetLatestTrackingStateSet().UserState.UnifiedScreenGaze`,
`TrackingConfidence`) was taken from docs.beam.eyeware.tech; if a newer SDK renames anything,
`BeamGazeSource.cs` is the only file to adjust.
