"""Reconstructs a mesh from a recorded session.

    python run_fusion.py <session_dir> [--estimator foundation] [--out mesh_dir]

Depth is estimated once per keyframe and integrated into both grids, so the expensive
step runs a single time. Writes a .ply and a decimated .glb per grid.
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import numpy as np
import open3d as o3d
import torch

import fusion
import keyframes
import rectify
from depth import estimator


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", type=Path)
    parser.add_argument("--estimator", default="foundation")
    parser.add_argument("--out", type=Path, default=Path("mesh"))
    parser.add_argument("--limit", type=int, default=0, help="use only the first N keyframes")
    parser.add_argument("--target-triangles", type=int, default=600000)
    parser.add_argument("--decay", type=float, default=1.0,
                        help="per-keyframe weight decay; below 1 lets moving objects fade")
    args = parser.parse_args()

    frames = keyframes.load_session(args.session)
    if args.limit:
        frames = frames[:args.limit]
    print(f"{len(frames)} keyframes, baseline {frames[0].baseline * 100:.2f} cm")

    # The rig is rigid, so rectification is computed once for the whole session.
    rectification = rectify.build(frames[0])
    estimate = estimator(args.estimator)

    started = time.time()
    results, colours, weights = [], [], []
    for n, frame in enumerate(frames):
        left, right, camera = rectification.apply(frame)
        result = estimate(left, right, camera, rectification.baseline)
        results.append(result)
        colours.append(left)
        weights.append(result.confidence * fusion.incidence_weight(result))
        print(f"  depth {frame.index:05d}  coverage {result.coverage * 100:5.1f}%  "
              f"({n + 1}/{len(frames)}, {time.time() - started:.0f}s)")

    low, high = fusion.bounds_from(frames, results)
    print(f"\nvolume {high[0] - low[0]:.2f} x {high[1] - low[1]:.2f} x {high[2] - low[2]:.2f} m")

    args.out.mkdir(parents=True, exist_ok=True)
    for spec in (fusion.NEAR, fusion.FAR):
        grid = fusion.Grid(spec, low, high)
        print(f"\n{spec.name}: {spec.voxel_size * 1000:.0f} mm voxels, "
              f"{grid.dims[0]}x{grid.dims[1]}x{grid.dims[2]} = {grid.voxel_count / 1e6:.1f}M voxels")

        for result, colour, weight in zip(results, colours, weights):
            if args.decay < 1.0:
                grid.decay(args.decay)
            grid.integrate(result, colour, weight)

        mesh = grid.mesh()
        if len(mesh.triangles) == 0:
            print("          empty")
            del grid
            torch.cuda.empty_cache()
            continue

        print(f"          {len(mesh.triangles)} triangles, {len(mesh.vertices)} vertices")
        o3d.io.write_triangle_mesh(str(args.out / f"{spec.name}.ply"), mesh)

        reduced = fusion.simplify(mesh, args.target_triangles)
        reduced.compute_vertex_normals()
        o3d.io.write_triangle_mesh(str(args.out / f"{spec.name}.glb"), reduced)
        print(f"          decimated to {len(reduced.triangles)} triangles")

        del grid
        torch.cuda.empty_cache()

    print(f"\nwritten to {args.out}")


if __name__ == "__main__":
    main()
