Closes #

**SRS:** <!-- requirement IDs this PR satisfies -->

## What changed

## How it was verified
- [ ] Tests written first; `dotnet test tests/CoGaze.Tests` passes
- [ ] `python scripts/check_architecture.py` passes
- [ ] Unity code touched → `scripts/run-unity-tests.ps1` run locally (EditMode, and PlayMode if relevant)
- [ ] Rehearsal run in the editor reaches the end of a session (expert) / shows cues (worker) when behaviour changed
- [ ] Pixels / passthrough touched → checked with real camera images (Link with Developer options, or on device)

## Rules
- [ ] Features reference only `CoGaze.Core` (+ `CoGaze.Geometry`); wiring only in `*.App`
- [ ] `CoGaze.Core` unchanged, or the change was agreed in an issue first
- [ ] Experiment values live in `config/`, not in C#
- [ ] New `.meta` files committed with their assets
