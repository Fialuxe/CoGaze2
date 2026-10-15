# CoGaze.Expert.Calibration

Calibration and validation (EXP-F-010..017, EXP-F-048). Pure C#, references only `CoGaze.Core`.

Exists: `CalibrationState` (the result the sigma policies read).

To build (#32, blocked by #30 / TBD-09):

- `CalibrationPlan` — points inside the video rect, head prompts (front / slight left / slight right), per-point duration, from `app.json`
- `SampleWindow` — discard the first 500 ms after a point appears, keep the next 1000 ms of `valid` samples
- `RidgeMapping` — estimator output (gaze vector, origin, head pose) → screen px, regularisation from config
- `Validation` — separate points; error in degrees via `ScreenGeometry`; `calibration.csv` / `calibration_result.json`
- Tobii and Beam keep their own calibration (EXP-F-017): the mapping applies only to `webcam:*`

The point display is a UI concern and goes in `CoGaze.Expert.UI`, driven by this assembly's state.
