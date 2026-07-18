"""Measures what image compression costs the depth estimate.

    python run_compression.py <session_dir> [--limit 8]

Sending raw stereo costs about 118 Mbps at two keyframes per second, which is most of a
headset's usable WiFi. That choice was made against block matching, where compression
artefacts directly corrupt the correlation. A learned matcher leans on monocular priors as
well, so it may tolerate far more — this measures whether it does, rather than assuming
either way.

Reports, per encoding: transmitted size and the implied bitrate, coverage, and how far the
depth moved from the raw result where both produced a value.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import cv2
import numpy as np

import keyframes
import rectify
from depth import estimator

KEYFRAMES_PER_SECOND = 2.0


def encode(image: np.ndarray, kind: str, quality: int) -> tuple[np.ndarray, int]:
    """Round-trips an image through an encoding, returning the result and its byte size."""
    if kind == "raw":
        return image, image.nbytes

    bgr = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
    if kind == "jpeg":
        ok, buffer = cv2.imencode(".jpg", bgr, [cv2.IMWRITE_JPEG_QUALITY, quality])
    elif kind == "png":
        ok, buffer = cv2.imencode(".png", bgr, [cv2.IMWRITE_PNG_COMPRESSION, 3])
    else:
        raise ValueError(kind)
    if not ok:
        raise RuntimeError(f"failed to encode as {kind}")

    decoded = cv2.imdecode(buffer, cv2.IMREAD_COLOR)
    return cv2.cvtColor(decoded, cv2.COLOR_BGR2RGB), int(buffer.nbytes)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", type=Path)
    parser.add_argument("--limit", type=int, default=8)
    args = parser.parse_args()

    frames = keyframes.load_session(args.session)[:args.limit]
    rectification = rectify.build(frames[0])
    estimate = estimator("foundation")

    encodings = [("raw", 0), ("png", 0), ("jpeg", 95), ("jpeg", 90), ("jpeg", 80), ("jpeg", 60)]
    reference: list[np.ndarray] = []

    print(f"{len(frames)} keyframes at {frames[0].width}x{frames[0].height}\n")
    print(f"{'encoding':<12}{'MB/keyframe':>13}{'Mbps @2Hz':>12}"
          f"{'coverage':>11}{'depth shift':>14}")

    for kind, quality in encodings:
        name = kind if not quality else f"{kind} q{quality}"
        coverages, shifts, total_bytes = [], [], 0

        for n, frame in enumerate(frames):
            left, right, camera = rectification.apply(frame)
            left_c, left_bytes = encode(left, kind, quality)
            right_c, right_bytes = encode(right, kind, quality)
            total_bytes += left_bytes + right_bytes

            result = estimate(left_c, right_c, camera, rectification.baseline)
            coverages.append(result.coverage)

            if kind == "raw":
                reference.append(result.depth)
            else:
                both = np.isfinite(result.depth) & np.isfinite(reference[n])
                if both.any():
                    shifts.append(np.median(np.abs(result.depth[both] - reference[n][both])))

        megabytes = total_bytes / len(frames) / 1e6
        mbps = megabytes * 8 * KEYFRAMES_PER_SECOND
        shift = f"{np.mean(shifts) * 1000:.2f} mm" if shifts else "reference"
        print(f"{name:<12}{megabytes:>13.2f}{mbps:>12.0f}"
              f"{np.mean(coverages) * 100:>10.1f}%{shift:>14}")


if __name__ == "__main__":
    main()
