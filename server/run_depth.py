"""Runs a depth estimator over a recorded session and reports what it produced.

    python run_depth.py <session_dir> [--estimator sgbm] [--out preview_dir]

Prints per-keyframe coverage and depth statistics, and writes a side-by-side preview
of the rectified left image and its depth so the result can be judged by eye.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import cv2
import numpy as np

import keyframes
import rectify
from depth import estimator


def preview(image: np.ndarray, depth: np.ndarray, near: float, far: float) -> np.ndarray:
    """Rectified image beside its depth, coloured near-to-far with gaps left black."""
    finite = np.isfinite(depth)
    normalised = np.zeros(depth.shape, dtype=np.uint8)
    if finite.any():
        clipped = np.clip(depth[finite], near, far)
        normalised[finite] = (255 * (1 - (clipped - near) / (far - near))).astype(np.uint8)

    coloured = cv2.applyColorMap(normalised, cv2.COLORMAP_TURBO)
    coloured[~finite] = 0
    return np.hstack([cv2.cvtColor(image, cv2.COLOR_RGB2BGR), coloured])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", type=Path)
    parser.add_argument("--estimator", default="sgbm")
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--near", type=float, default=0.3)
    parser.add_argument("--far", type=float, default=3.0)
    args = parser.parse_args()

    frames = keyframes.load_session(args.session)
    if not frames:
        raise SystemExit(f"no keyframes in {args.session}")
    print(f"{len(frames)} keyframes, {frames[0].width}x{frames[0].height}, "
          f"baseline {frames[0].baseline * 100:.2f} cm")

    # The rig is rigid, so one rectification serves the whole session.
    rectification = rectify.build(frames[0])
    print(f"rectified: fx={rectification.fx:.2f} cx={rectification.cx:.2f} "
          f"cy={rectification.cy:.2f} baseline={rectification.baseline * 100:.2f} cm")

    estimate = estimator(args.estimator)
    if args.out:
        args.out.mkdir(parents=True, exist_ok=True)

    coverages = []
    for frame in frames:
        left, right, camera = rectification.apply(frame)
        result = estimate(left, right, camera, rectification.baseline)

        finite = np.isfinite(result.depth)
        coverages.append(result.coverage)
        if finite.any():
            values = result.depth[finite]
            span = f"{np.percentile(values, 5):.2f}-{np.percentile(values, 95):.2f} m"
        else:
            span = "no depth"
        print(f"  {frame.index:05d}  coverage {result.coverage * 100:5.1f}%  {span}")

        if args.out:
            cv2.imwrite(str(args.out / f"{frame.index:05d}.png"),
                        preview(left, result.depth, args.near, args.far))

    print(f"\nmean coverage {np.mean(coverages) * 100:.1f}% "
          f"(min {np.min(coverages) * 100:.1f}%, max {np.max(coverages) * 100:.1f}%)")
    if args.out:
        print(f"previews in {args.out}")


if __name__ == "__main__":
    main()
