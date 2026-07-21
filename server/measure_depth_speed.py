"""What does FoundationStereo cost, and what does cutting it buy?

    python measure_depth_speed.py <session> [--frames 3]

The live loop is bound by depth: ~4 s a frame at the demo settings, too slow to update while
the worker works. Two knobs cut that without new weights — the number of refinement iterations,
and the input resolution. This measures both against coverage and against how far the depth moves
from the full-quality result, so the trade can be made by looking rather than guessing.
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import cv2
import numpy as np

import fusion
import keyframes
import rectify
from depth import foundation_stereo
from keyframes import Camera

ITERS = [32, 16, 8, 4]
SCALES = [1.0, 0.5]


def scaled(camera: Camera, factor: float) -> Camera:
    return Camera(fx=camera.fx * factor, fy=camera.fy * factor,
                  cx=camera.cx * factor, cy=camera.cy * factor, pose=camera.pose)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", nargs="?", default="../recordings/session01", type=Path)
    parser.add_argument("--frames", type=int, default=3)
    args = parser.parse_args()

    frames = keyframes.load_session(args.session)[:args.frames]
    rectification = rectify.build(frames[0])
    rectified = [rectification.apply(f) for f in frames]

    # Reference: full resolution, full iterations. Depth shift is measured against this.
    foundation_stereo.VALID_ITERS = 32
    reference = [foundation_stereo.estimate(l, r, c, rectification.baseline)
                 for l, r, c in rectified]

    print(f"{len(frames)} frames at {frames[0].width}x{frames[0].height}, "
          f"baseline {rectification.baseline * 100:.2f} cm\n")
    print(f"{'iters':>6}{'scale':>7}{'s/frame':>9}{'Hz':>6}{'coverage':>10}{'vs 32/full':>12}")

    for scale in SCALES:
        for iters in ITERS:
            foundation_stereo.VALID_ITERS = iters
            times, coverages, shifts = [], [], []
            for (left, right, camera), ref in zip(rectified, reference):
                if scale != 1.0:
                    size = (int(left.shape[1] * scale), int(left.shape[0] * scale))  # (w, h)
                    left_s = cv2.resize(left, size, interpolation=cv2.INTER_AREA)
                    right_s = cv2.resize(right, size, interpolation=cv2.INTER_AREA)
                    cam_s = scaled(camera, scale)
                else:
                    left_s, right_s, cam_s = left, right, camera

                start = time.time()
                result = foundation_stereo.estimate(left_s, right_s, cam_s, rectification.baseline)
                times.append(time.time() - start)
                coverages.append(result.coverage)

                # Compare depth to the reference at full resolution.
                depth = result.depth
                if scale != 1.0:
                    depth = cv2.resize(depth, (ref.depth.shape[1], ref.depth.shape[0]),
                                       interpolation=cv2.INTER_NEAREST)
                both = np.isfinite(depth) & np.isfinite(ref.depth)
                if both.any():
                    shifts.append(np.median(np.abs(depth[both] - ref.depth[both])))

            seconds = float(np.mean(times))
            shift = f"{np.mean(shifts) * 1000:.1f} mm" if shifts else "-"
            print(f"{iters:>6}{scale:>7.2f}{seconds:>9.2f}{1 / seconds:>6.1f}"
                  f"{np.mean(coverages) * 100:>9.1f}%{shift:>12}")


if __name__ == "__main__":
    main()
