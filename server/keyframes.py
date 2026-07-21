"""Reading stereo keyframes recorded by the headset."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np


@dataclass(frozen=True)
class Camera:
    """A pinhole camera and where it sits in the world.

    The convention throughout is right-handed with Y down, and ``pose`` maps camera
    coordinates into world coordinates. The headset applies this conversion before
    recording, so nothing here has to know about Unity's left-handed frame.
    """

    fx: float
    fy: float
    cx: float
    cy: float
    pose: np.ndarray  # 4x4, world_T_camera

    @property
    def matrix(self) -> np.ndarray:
        """Intrinsics as a 3x3 matrix."""
        return np.array([[self.fx, 0.0, self.cx],
                         [0.0, self.fy, self.cy],
                         [0.0, 0.0, 1.0]], dtype=np.float64)


@dataclass(frozen=True)
class Keyframe:
    index: int
    epoch: int
    width: int
    height: int
    left: Camera
    right: Camera
    left_rgb: np.ndarray   # (h, w, 3) uint8
    right_rgb: np.ndarray

    @property
    def baseline(self) -> float:
        """Distance between the two camera centres, in metres."""
        return float(np.linalg.norm(self.left.pose[:3, 3] - self.right.pose[:3, 3]))


def _pose_matrix(position, quaternion_xyzw) -> np.ndarray:
    x, y, z, w = quaternion_xyzw
    rotation = np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ], dtype=np.float64)

    pose = np.eye(4, dtype=np.float64)
    pose[:3, :3] = rotation
    pose[:3, 3] = position
    return pose


def _camera(entry: dict) -> Camera:
    return Camera(fx=entry["fx"], fy=entry["fy"], cx=entry["cx"], cy=entry["cy"],
                  pose=_pose_matrix(entry["position"], entry["quaternion_xyzw"]))


def load(path: Path | str) -> Keyframe:
    """Loads one keyframe given the path to its .json; images sit alongside it."""
    path = Path(path)
    # utf-8-sig so recordings made before the byte order mark was removed still read.
    meta = json.loads(path.read_text(encoding="utf-8-sig"))

    shape = (meta["height"], meta["width"], 3)
    stem = path.with_suffix("")

    def image(suffix: str) -> np.ndarray:
        # Recordings write raw RGB; the live service logs JPEG to keep the log small. Either
        # replays the same way, so both are accepted and a live log needs no conversion.
        raw = Path(f"{stem}_{suffix}.raw")
        if raw.exists():
            return np.fromfile(raw, dtype=np.uint8).reshape(shape)
        import cv2
        bgr = cv2.imread(f"{stem}_{suffix}.jpg", cv2.IMREAD_COLOR)
        return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

    return Keyframe(
        index=meta["index"],
        epoch=meta["epoch"],
        width=meta["width"],
        height=meta["height"],
        left=_camera(meta["left"]),
        right=_camera(meta["right"]),
        left_rgb=image("L"),
        right_rgb=image("R"),
    )


def load_session(directory: Path | str) -> list[Keyframe]:
    """Loads every keyframe in a recording, ordered by index."""
    directory = Path(directory)
    return [load(p) for p in sorted(directory.glob("*.json"))]
