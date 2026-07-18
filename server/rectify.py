"""Stereo rectification.

Rectification is shared by every depth estimator, so it lives outside them. It also
changes the camera the depth belongs to: the rectified view has different intrinsics
and a rotated pose. Both are returned alongside the images, because fusing depth
against the *original* camera parameters is a silent and hard-to-trace error.

The relative pose between the two cameras is fixed by the hardware, so the maps are
computed once and reused.
"""

from __future__ import annotations

from dataclasses import dataclass

import cv2
import numpy as np

from keyframes import Camera, Keyframe


@dataclass(frozen=True)
class Rectification:
    left_map: tuple[np.ndarray, np.ndarray]
    right_map: tuple[np.ndarray, np.ndarray]
    fx: float
    fy: float
    cx: float
    cy: float
    baseline: float
    """Rotation from the original left camera into the rectified one."""
    left_rotation: np.ndarray  # 3x3

    def apply(self, keyframe: Keyframe) -> tuple[np.ndarray, np.ndarray, Camera]:
        """Rectifies a keyframe's images and returns the camera the result belongs to."""
        left = cv2.remap(keyframe.left_rgb, *self.left_map, cv2.INTER_LINEAR)
        right = cv2.remap(keyframe.right_rgb, *self.right_map, cv2.INTER_LINEAR)

        # Rectification rotates about the camera centre, so the position is unchanged
        # and only the orientation moves.
        pose = keyframe.left.pose.copy()
        pose[:3, :3] = keyframe.left.pose[:3, :3] @ self.left_rotation.T

        camera = Camera(fx=self.fx, fy=self.fy, cx=self.cx, cy=self.cy, pose=pose)
        return left, right, camera


def build(keyframe: Keyframe) -> Rectification:
    """Derives rectification maps from the relative pose of the two cameras.

    No distortion coefficients are passed: the SDK exposes none, so the cameras are
    treated as pinhole.
    """
    size = (keyframe.width, keyframe.height)

    # stereoRectify wants the transform taking left camera coordinates into right
    # camera coordinates, whereas the poses map camera coordinates into the world.
    left_to_world = keyframe.left.pose
    right_to_world = keyframe.right.pose
    rotation = right_to_world[:3, :3].T @ left_to_world[:3, :3]
    translation = right_to_world[:3, :3].T @ (left_to_world[:3, 3] - right_to_world[:3, 3])

    zero_distortion = np.zeros(5)
    r_left, r_right, p_left, p_right, _, _, _ = cv2.stereoRectify(
        keyframe.left.matrix, zero_distortion,
        keyframe.right.matrix, zero_distortion,
        size, rotation, translation,
        flags=cv2.CALIB_ZERO_DISPARITY, alpha=0,
    )

    left_map = cv2.initUndistortRectifyMap(
        keyframe.left.matrix, zero_distortion, r_left, p_left, size, cv2.CV_32FC1)
    right_map = cv2.initUndistortRectifyMap(
        keyframe.right.matrix, zero_distortion, r_right, p_right, size, cv2.CV_32FC1)

    fx = float(p_left[0, 0])
    # P2 holds the baseline as -fx * B in its translation term.
    baseline = float(-p_right[0, 3] / fx)

    return Rectification(
        left_map=left_map,
        right_map=right_map,
        fx=fx,
        fy=float(p_left[1, 1]),
        cx=float(p_left[0, 2]),
        cy=float(p_left[1, 2]),
        baseline=baseline,
        left_rotation=np.asarray(r_left, dtype=np.float64),
    )
