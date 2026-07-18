"""Semi-global block matching.

Kept as a baseline rather than as the intended estimator. It leaves roughly a quarter
of pixels without a value on textureless surfaces, which this system cannot accept, so
its role is to quantify what a learned estimator buys.
"""

from __future__ import annotations

import cv2
import numpy as np

from depth import DepthResult
from keyframes import Camera

BLOCK_SIZE = 5

# Nearest distance the matcher should reach. Disparity is fx*B/Z, so this and the rig
# together fix the search range; hard-coding the range instead would silently mismatch any
# rig with a different baseline or focal length.
MIN_DISTANCE = 0.35


def disparity_range(fx: float, baseline: float, min_distance: float = MIN_DISTANCE) -> int:
    """Search range covering everything beyond ``min_distance``, rounded up to the
    multiple of 16 that StereoSGBM requires."""
    needed = fx * baseline / min_distance
    return max(16, int(np.ceil(needed / 16)) * 16)

# The smoothness penalties are conventionally scaled by channels * blockSize^2. Passing
# raw constants instead is a common mistake that quietly under-smooths.
_CHANNELS = 1
P1 = 8 * _CHANNELS * BLOCK_SIZE ** 2
P2 = 32 * _CHANNELS * BLOCK_SIZE ** 2


def estimate(left: np.ndarray, right: np.ndarray, camera: Camera, baseline: float) -> DepthResult:
    matcher = cv2.StereoSGBM_create(
        minDisparity=0,
        numDisparities=disparity_range(camera.fx, baseline),
        blockSize=BLOCK_SIZE,
        P1=P1,
        P2=P2,
        disp12MaxDiff=1,
        uniquenessRatio=10,
        speckleWindowSize=100,
        speckleRange=2,
        mode=cv2.STEREO_SGBM_MODE_SGBM_3WAY,
    )

    gray_left = cv2.cvtColor(left, cv2.COLOR_RGB2GRAY)
    gray_right = cv2.cvtColor(right, cv2.COLOR_RGB2GRAY)

    # StereoSGBM returns disparity as a fixed-point value scaled by 16.
    disparity = matcher.compute(gray_left, gray_right).astype(np.float32) / 16.0

    valid = disparity > 0
    depth = np.full(disparity.shape, np.inf, dtype=np.float32)
    np.divide(camera.fx * baseline, disparity, out=depth, where=valid)

    # No per-pixel confidence is available, so this only marks where a value exists.
    confidence = valid.astype(np.float32)

    return DepthResult(depth=depth, confidence=confidence, camera=camera)
