"""Writes a synthetic workbench as .cgzm chunk updates, so the expert's 3D view can be seen
without the GPU server or a recording.

    python make_synthetic_chunks.py [--out ../recordings/synthetic_chunks]

Needs only numpy and DracoPy (no torch): ``pip install numpy DracoPy`` in a venv. Then set
``"reconstruction": { "staging_dir": "recordings/synthetic_chunks" }`` in config/app.json.

The bytes come from the real encoder (``chunks.Update.encode`` and ``chunks._compress``), so the
client cannot tell them from a replayed session. The scene is authored as the expert should see it
(Unity: +X right, +Y up, +Z forward from the worker, metres, eyes at the origin) and converted to the
wire frame with the inverse of ``MeshReceiver.ToUnityFrame`` — negate X and Y — so it arrives with
the same left-right mirror the real reconstruction has and shows the right way round. The red mug
is on the worker's left, the green box on the right: if they swap, the X correction is broken.

The updates build up the way a live session does — room, then desk, then objects — and the last two
move the screwdriver (chunks replaced) and clear a paper roll (chunks removed), so replacement
and removal are exercised too.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from types import SimpleNamespace

import numpy as np

from chunks import Update, _compress

CHUNK_M = 0.25            # bucket size for splitting geometry into chunks (the server uses 12.8 cm)
RNG = np.random.default_rng(7)


class Part:
    """Vertices (Unity frame), triangles and per-vertex RGB in 0..1."""

    def __init__(self, vertices, triangles, rgb):
        self.v = np.asarray(vertices, dtype=np.float64)
        self.t = np.asarray(triangles, dtype=np.int64)
        self.c = np.broadcast_to(np.asarray(rgb, dtype=np.float64), self.v.shape).copy()

    @staticmethod
    def join(parts):
        v, t, c, base = [], [], [], 0
        for p in parts:
            v.append(p.v)
            t.append(p.t + base)
            c.append(p.c)
            base += len(p.v)
        return Part(np.concatenate(v), np.concatenate(t), np.concatenate(c))


def grid(origin, u, w, nu, nw, rgb, checker=None):
    """A subdivided quad origin + a*u + b*w; subdivided so it splits into chunks and shades like a scan."""
    a, b = np.meshgrid(np.linspace(0, 1, nu + 1), np.linspace(0, 1, nw + 1), indexing="ij")
    v = np.asarray(origin) + a[..., None] * np.asarray(u) + b[..., None] * np.asarray(w)
    idx = np.arange((nu + 1) * (nw + 1)).reshape(nu + 1, nw + 1)
    q = np.stack([idx[:-1, :-1], idx[1:, :-1], idx[1:, 1:], idx[:-1, 1:]], -1).reshape(-1, 4)
    tris = np.concatenate([q[:, [0, 1, 2]], q[:, [0, 2, 3]]])
    rgb = np.broadcast_to(np.asarray(rgb, dtype=np.float64), a.shape + (3,)).copy()
    if checker is not None:
        rgb[(np.floor(a * nu / 2) + np.floor(b * nw / 2)) % 2 == 1] = checker
    return Part(v.reshape(-1, 3), tris, rgb.reshape(-1, 3))


def box(center, size, rgb):
    cx, cy, cz = center
    sx, sy, sz = np.asarray(size) / 2
    faces = []
    for axis in range(3):
        for sign in (-1, 1):
            o = np.array([cx, cy, cz], dtype=np.float64)
            half = np.array([sx, sy, sz])
            o[axis] += sign * half[axis]
            others = [i for i in range(3) if i != axis]
            u = np.zeros(3); u[others[0]] = 2 * half[others[0]]
            w = np.zeros(3); w[others[1]] = 2 * half[others[1]]
            corner = o - u / 2 - w / 2
            shade = 0.82 + 0.06 * axis + (0.06 if sign > 0 else 0)     # faces read as distinct
            faces.append(grid(corner, u, w, 4, 4, np.asarray(rgb) * shade))
    return Part.join(faces)


def cylinder(base, radius, height, rgb, segments=24, rings=6):
    ang = np.linspace(0, 2 * np.pi, segments, endpoint=False)
    ys = np.linspace(0, height, rings + 1)
    v = np.array([[base[0] + radius * np.cos(t), base[1] + y, base[2] + radius * np.sin(t)] for y in ys for t in ang])
    tris = []
    for r in range(rings):
        for s in range(segments):
            a, b = r * segments + s, r * segments + (s + 1) % segments
            tris += [[a, b, b + segments], [a, b + segments, a + segments]]
    top = len(v)
    v = np.vstack([v, [base[0], base[1] + height, base[2]]])
    tris += [[(rings) * segments + s, (rings) * segments + (s + 1) % segments, top] for s in range(segments)]
    shade = 0.75 + 0.25 * (0.5 + 0.5 * np.cos(np.r_[np.tile(ang, rings + 1), 0] - 2.4))
    return Part(v, tris, np.asarray(rgb) * shade[:, None])


def scene():
    """Update groups in the order they arrive: (name, parts). Eyes at the origin, desk in front."""
    floor_y, desk_y = -1.25, -0.5
    room = [grid([-1.5, floor_y, -0.3], [3.0, 0, 0], [0, 0, 2.6], 24, 20, [0.42, 0.42, 0.45], checker=[0.36, 0.36, 0.39]),
            grid([-1.5, floor_y, 2.3], [3.0, 0, 0], [0, 2.0, 0], 24, 16, [0.70, 0.68, 0.62])]
    desk = [box([0, desk_y - 0.02, 0.75], [1.4, 0.04, 0.7], [0.62, 0.45, 0.28]),
            box([-0.62, (floor_y + desk_y) / 2, 0.75], [0.05, desk_y - floor_y, 0.6], [0.3, 0.3, 0.32]),
            box([0.62, (floor_y + desk_y) / 2, 0.75], [0.05, desk_y - floor_y, 0.6], [0.3, 0.3, 0.32]),
            grid([-0.25, desk_y + 0.001, 0.55], [0.5, 0, 0], [0, 0, 0.35], 6, 4, [0.15, 0.32, 0.22])]   # cutting mat
    objects = [
        ("mug (left, red)", [cylinder([-0.45, desk_y, 0.7], 0.045, 0.1, [0.85, 0.18, 0.16])]),
        ("box (right, green)", [box([0.42, desk_y + 0.06, 0.8], [0.16, 0.12, 0.12], [0.2, 0.7, 0.3])]),
        ("board (centre, blue)", [box([0.0, desk_y + 0.015, 0.72], [0.22, 0.03, 0.14], [0.2, 0.35, 0.8])]),
        ("screwdriver", [box([0.12, desk_y + 0.012, 0.6], [0.18, 0.02, 0.025], [0.95, 0.75, 0.1])]),
        ("scrap", [cylinder([-0.15, desk_y, 0.95], 0.03, 0.35, [0.9, 0.9, 0.88])]),      # a tall roll: its top chunk empties
    ]
    return [("room", room), ("desk", desk)] + objects


def to_wire(v):
    """Inverse of MeshReceiver.ToUnityFrame: (x, y, z) -> (-x, -y, z), which is its own inverse."""
    return v * np.array([-1.0, -1.0, 1.0])


def chunk(part):
    """Splits a part into chunks by triangle centroid; returns {key: Part}."""
    keys = np.floor(part.v[part.t].mean(axis=1) / CHUNK_M).astype(int)
    out = {}
    for key in {tuple(k) for k in keys}:
        tris = part.t[(keys == key).all(axis=1)]
        used, inverse = np.unique(tris, return_inverse=True)
        out[key] = Part(part.v[used], inverse.reshape(-1, 3), part.c[used])
    return out


def encode(part):
    """Through the real encoder; colour jitter stands in for the noise a scan has."""
    colours = np.clip(part.c + RNG.normal(0, 0.02, part.c.shape), 0, 1)
    mesh = SimpleNamespace(vertices=to_wire(part.v), triangles=part.t, vertex_colors=colours)
    return _compress(SimpleNamespace(mesh=mesh, confidence=np.full(len(part.v), 0.9)))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parent.parent / "recordings" / "synthetic_chunks")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("*.cgzm"):
        old.unlink()

    world = {}            # key -> {group name: part}; a chunk shared by several groups is re-sent whole
    written = 0

    def emit(name, changes):
        """Applies {key: (group, part or None)} to the world and writes one update for those chunks."""
        nonlocal written
        for key, (group, piece) in changes.items():
            names = world.setdefault(key, {})
            if piece is None:
                names.pop(group, None)
            else:
                names[group] = piece
        update = Update(epoch=0)
        for key in sorted(changes):
            if world[key]:
                merged = Part.join(list(world[key].values()))
                update.replaced.append(("near", key, encode(merged), bytes(len(merged.v))))
            else:
                update.removed.append(("near", key))
                del world[key]
        path = args.out / f"{written:05d}.cgzm"
        path.write_bytes(update.encode())
        written += 1
        print(f"{path.name}  {name:22s} {len(update.replaced):3d} replaced  {len(update.removed)} removed  "
              f"{path.stat().st_size / 1024:6.1f} KB")

    for name, parts in scene():
        emit(name, {k: (name, p) for k, p in chunk(Part.join(parts)).items()})

    # The screwdriver moves: its old chunks are re-sent without it, its new ones with it.
    changes = {k: ("screwdriver", None) for k, names in world.items() if "screwdriver" in names}
    changes.update({k: ("screwdriver", p) for k, p in
                    chunk(box([0.3, -0.5 + 0.012, 0.55], [0.18, 0.02, 0.025], [0.95, 0.75, 0.1])).items()})
    emit("screwdriver moved", changes)

    # The roll is cleared: chunks it alone occupied are removed, the rest re-sent without it.
    emit("scrap cleared", {k: ("scrap", None) for k, names in world.items() if "scrap" in names})
    print(f"\nwrote {written} updates to {args.out}")


if __name__ == "__main__":
    main()
