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

# 160 covers disparities down to about 0.35 m for this rig, and is a multiple of 16 as
# StereoSGBM requires.
NUM_DISPARITIES = 160
BLOCK_SIZE = 5

# The smoothness penalties are conventionally scaled by channels * blockSize^2. Passing
# raw constants instead is a common mistake that quietly under-smooths.
_CHANNELS = 1
P1 = 8 * _CHANNELS * BLOCK_SIZE ** 2
P2 = 32 * _CHANNELS * BLOCK_SIZE ** 2


def estimate(left: np.ndarray, right: np.ndarray, camera: Camera, baseline: float) -> DepthResult:
    matcher = cv2.StereoSGBM_create(
        minDisparity=0,
        numDisparities=NUM_DISPARITIES,
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
