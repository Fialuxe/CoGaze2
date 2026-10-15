# Working rules

These rules exist so the system stays runnable by a small team against a fixed deadline (main
experiment, January 2027), and so most of them can be checked by a machine rather than by memory.
Sources: `docs/CoGaze2_Unity実装詳細.md` (how), `docs/CoGaze_SRS.md` (what).

## 1. Picking work

1. Work comes from GitHub Issues. Each issue has a **milestone** (when), a **priority** label (how
   much it matters) and **Blocked by** links (what must close first).
2. Take the highest-priority open issue in the earliest open milestone whose blockers are all closed.
   `P0-critical` is the path to the experiment; never start `P2`/`P3` while a reachable `P0` is open.
3. An issue labelled `type:decision` is closed by writing the answer into the SRS or `config/`, not
   by code. Implementation issues blocked by it wait.
4. One issue = one assembly or one capability = one branch `feat/<issue#>-<slug>` = one PR with
   `Closes #n`. Keep PRs reviewable in one sitting.

## 2. Definition of done

An issue is done when **all** of these hold:

- the tests named in the issue were written **first** and pass in CI;
- `python scripts/check_architecture.py` passes;
- Unity-side changes were checked with `scripts/run-unity-tests.ps1` and, when behaviour changed,
  a rehearsal run in the editor;
- anything touching camera pixels was checked with real camera images (over Link with Developer
  options enabled, or on device);
- the PR template checklist is complete.

## 3. Feedback loop

Fastest first; run the cheap ones constantly.

| Loop | Command | Time | What it catches |
|---|---|---|---|
| Rules | `python scripts/check_architecture.py` | < 1 s | asmdef direction, Assembly-CSharp leaks, files over 300 lines, scene search, static state, pure assemblies missing from the test build |
| Pure tests | `dotnet test tests/CoGaze.Tests` | ~10 s | Core, Clock, Logging, Config, Protocol, Loopback, Pipeline (incl. **VT-01**), Experiment, OSC, View2D, Cue — compiled as Unity compiles them |
| CI | push / PR → `.github/workflows/ci.yml` | ~2 min | both of the above, on every change |
| Unity tests | `scripts/run-unity-tests.ps1 -Project expert-app` | minutes | Unity-dependent code, PlayMode |
| Rehearsal | Play `Expert.unity` with `"rehearsal": true` | 1 session | the whole expert path end to end with fakes; the checklist shows what is wrong |
| Device | build to Quest | — | passthrough pixels, alignment, frame rate |

VT-01 (only the condition's display source ever reaches the worker) runs in CI on every push, as the
SRS requires it after every change.

## 4. Code rules

Checked by `scripts/check_architecture.py` where marked ✓.

- **One folder = one asmdef = one role.** No code in Assembly-CSharp. ✓
- **Features talk only through `CoGaze.Core`** (plus `CoGaze.Geometry` for Unity↔maths conversion).
  Features never reference each other; the `*.App` bootstrap is the only place that picks concrete
  parts and connects them. ✓
- **Pure where possible.** Anything that does not need UnityEngine sets `noEngineReferences: true`
  and is added to `tests/CoGaze.Pure/CoGaze.Pure.csproj`, so it is built and tested without Unity. ✓
- **Per-frame logic is a plain C# class** (`GazePipeline`, `CuePipeline`, `TrialRunner`) with a
  `Tick`; MonoBehaviours only call it.
- **No singletons, no scene search** (`FindObjectOfType` & co.), **no mutable static state.** ✓
- **One class per file; split above 300 lines.** ✓ (size)
- **Data types are `readonly struct`s.** Values do not change in flight, so logs explain what happened.
- **Configuration lives in `config/`** (`app.json` for developers, `conditions.json` for experimenters).
  Changing a condition never means editing C#.
- **Failures are shown, never silently corrected:** a red checklist row (what happened + what to do,
  one sentence each) and a row in `events.csv`. Invalid config stops start-up and lists every problem.
- **Time** in logs is always `IClock.NowNs` (int64 ns), stamped right after acquisition. Never
  `Time.time` or `DateTime.Now` in data.
- **CSV columns** are defined once in `CoGaze.Core.LogSchema`, with units in the names.
- **Naming:** namespace = assembly name; classes are *what + role* (`BeamGazeSource`,
  `FixedSigmaPolicy`); MonoBehaviours end in `Behaviour`; values with units carry them
  (`SigmaDeg`, `TCaptureNs`, `DistanceM`).

## 5. Changing `CoGaze.Core`

Every feature depends on Core, so a change there can break everything at once. Open an issue
labelled `area:shared` + `type:decision` describing the change and its callers **before** editing.

## 6. Unity specifics

- Unity 6000.0.x. Commit `.meta` files with their assets; never commit `Library/`.
- The shared package is referenced as `file:../../packages/com.cogaze.shared` from both projects and
  listed under `testables` so its tests run in each.
- Beam SDK and model weights are never committed (licence / size). See
  `expert-app/Assets/ThirdParty/Beam/README.md`.

## 7. Commits

Author `fialuxe`. Imperative subject, body says why. No `Co-Authored-By` line.
