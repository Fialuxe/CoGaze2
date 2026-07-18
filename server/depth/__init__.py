"""Depth estimation from a rectified stereo pair.

Every estimator takes the same inputs and returns the same thing, so swapping one for
another is a change of name and nothing else. Estimators are plain functions; there is
no base class to inherit and no registry to register with.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable

import numpy as np

from keyframes import Camera


@dataclass(frozen=True)
class DepthResult:
    depth: np.ndarray
    """Metres, (h, w) float32. Non-finite where no depth was produced."""

    confidence: np.ndarray
    """(h, w) float32 in [0, 1], for weighting fusion. Estimators without a real
    confidence signal report 1 where depth exists and 0 elsewhere."""

    camera: Camera
    """The camera this depth belongs to — the rectified one, not the original."""

    @property
    def coverage(self) -> float:
        """Fraction of pixels carrying a depth value. The headline quality number for
        this system, which needs meshes without holes."""
        return float(np.isfinite(self.depth).mean())


Estimator = Callable[[np.ndarray, np.ndarray, Camera, float], DepthResult]


def estimator(name: str) -> Estimator:
    """Looks up an estimator by name. Imports lazily so that a heavyweight backend's
    dependencies are only needed when that backend is actually used."""
    if name == "sgbm":
        from depth.sgbm import estimate
        return estimate
    raise ValueError(f"unknown depth estimator: {name}")
