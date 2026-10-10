# config/

Read at start-up from `config/` next to the executable (in the editor: this folder). Edit with a
text editor; no rebuild needed. Both files are copied into every session's `session.json`.

| File | Who edits | What |
|---|---|---|
| `app.json` | developer | network, monitor, video rect, cue rate, filter profiles, sigma defaults, rehearsal switch |
| `conditions.json` | experimenter | condition list and per-participant order |

Invalid files stop start-up and every problem is listed on the checklist screen. Nothing silently
falls back to a default.

`conditions.json`:

- `display_source`: `beam`, `tobii`, `webcam:<estimator>`, `replay` or `none`
- `visualization`: `ray`, `circle`, `frustum`
- `sigma_source`: `fixed` (needs `fixed_sigma`, degrees), `calib`, `calib_live`
- `filter`: a profile name from `app.json` `filters`
- `orders.<participant>`: entries `{condition_id, kind}`; `kind` is `tutorial`, `practice` or `main`

`rehearsal: true` replaces Beam/Tobii with fake gaze and the worker with an in-process loopback.
