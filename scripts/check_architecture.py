"""Checks the working rules that can be checked mechanically (CONTRIBUTING.md, "Code rules").

    python scripts/check_architecture.py

Exit 1 with one line per violation. Runs in CI next to the tests.
"""
import json
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
SHARED = REPO / "packages/com.cogaze.shared/Runtime"
APPS = [REPO / "expert-app/Assets/Scripts", REPO / "worker-app/Assets/Scripts"]
PURE_CSPROJ = REPO / "tests/CoGaze.Pure/CoGaze.Pure.csproj"
MAX_LINES = 300

# Code that predates the gaze architecture and keeps its own conventions (see CLAUDE.md).
LEGACY = [REPO / "worker-app/Assets/Scripts/EnvLine", REPO / "worker-app/Assets/Scripts/GeometryProbe.cs"]
FEATURE_MAY_USE = {"CoGaze.Core", "CoGaze.Geometry"}
SHARED_MAY_USE = {"CoGaze.Core", "CoGaze.Protocol"}
FORBIDDEN = re.compile(r"\b(FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType|"
                       r"FindObjectsByType|GameObject\.Find)\b")

errors = []


def rel(p):
    return p.relative_to(REPO).as_posix()


def legacy(p):
    return any(p == l or l in p.parents for l in LEGACY)


def is_test(p):
    return "Tests" in p.parts


def asmdefs():
    for root in [SHARED, *APPS]:
        for a in root.rglob("*.asmdef"):
            if not legacy(a):
                yield a, json.loads(a.read_text(encoding="utf-8"))


def check_references():
    for path, a in asmdefs():
        name, refs = a["name"], [r for r in a.get("references", []) if r.startswith("CoGaze")]
        if SHARED in path.parents:
            if name == "CoGaze.Core" and (a.get("references") or not a.get("noEngineReferences")):
                errors.append(f"{rel(path)}: CoGaze.Core must have no references and noEngineReferences: true")
            bad = [r for r in refs if r not in SHARED_MAY_USE]
            if bad:
                errors.append(f"{rel(path)}: shared assembly {name} may reference only {sorted(SHARED_MAY_USE)}, not {bad}")
        elif not (is_test(path) or name.endswith(".App") or name.endswith(".App.Editor")):
            # A feature may also use its own sub-assemblies (e.g. View3D -> View3D.Model, its pure part).
            bad = [r for r in refs if r not in FEATURE_MAY_USE and not r.startswith(name + ".")]
            if bad:
                errors.append(f"{rel(path)}: feature {name} may reference only {sorted(FEATURE_MAY_USE)}, not {bad} "
                              "; talk through CoGaze.Core and wire in the App bootstrap")


def check_pure_coverage():
    csproj = PURE_CSPROJ.read_text(encoding="utf-8").replace("\\", "/")
    includes = re.findall(r'Include="\$\((\w+)\)([^"*]+)', csproj)
    roots = {"Shared": SHARED, "Expert": APPS[0], "Worker": APPS[1]}
    covered = {(roots[k] / v.rstrip("/")).resolve() for k, v in includes}
    for path, a in asmdefs():
        if is_test(path) or not a.get("noEngineReferences") or a.get("defineConstraints"):
            continue
        if path.parent.resolve() not in covered:
            errors.append(f"{rel(path)}: pure assembly {a['name']} is not compiled by {rel(PURE_CSPROJ)}; add its folder")


def owning_asmdef(cs, root):
    for d in cs.parents:
        if list(d.glob("*.asmdef")):
            return True
        if d == root:
            return False
    return False


def check_sources():
    for root in [SHARED, *APPS]:
        for cs in root.rglob("*.cs"):
            if legacy(cs):
                continue
            if not owning_asmdef(cs, root):
                errors.append(f"{rel(cs)}: not inside a folder with an asmdef (would land in Assembly-CSharp)")
            text = cs.read_text(encoding="utf-8")
            if is_test(cs):
                continue
            lines = text.count("\n") + 1
            if lines > MAX_LINES:
                errors.append(f"{rel(cs)}: {lines} lines; split above {MAX_LINES}")
            for i, line in enumerate(text.splitlines(), 1):
                code = line.split("//")[0]
                if FORBIDDEN.search(code):
                    errors.append(f"{rel(cs)}:{i}: scene search is not allowed; pass the reference in from the bootstrap")
                if re.search(r"\bstatic\b", code) and not re.search(r"\b(readonly|const|class|struct|using|extern)\b|\(|=>", code):
                    errors.append(f"{rel(cs)}:{i}: mutable static field; keep state in instances")


check_references()
check_pure_coverage()
check_sources()
for e in errors:
    print(e)
print(f"{len(errors)} violation(s)" if errors else "architecture rules: OK")
sys.exit(1 if errors else 0)
