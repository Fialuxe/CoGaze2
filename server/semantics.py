"""Naming what the geometry is.

Segments each keyframe in 2D and hands back a label per pixel, so the fusion step can
carry names into the volume alongside colour. At about 20 ms a frame this is negligible
beside depth estimation, which takes two hundred times longer.

Labels come from a closed 80-class vocabulary, which covers a workbench better than it
sounds — bottle, mouse, keyboard, clock, book, scissors — but names nothing outside it.
Anything unrecognised stays unlabelled rather than being forced into the nearest class.

A caveat worth knowing when reading results: a picture of an object is detected as the
object. A photograph on a monitor yields whatever it depicts, because 2D segmentation has
no notion of a surface being a depiction.
"""

from __future__ import annotations

import numpy as np

MODEL = "yolo11l-seg.pt"
CONFIDENCE = 0.35

UNLABELLED = 0
"""Reserved so labels can be stored one-based and zero means "not named"."""

_model = None


def _load():
    global _model
    if _model is None:
        from ultralytics import YOLO
        _model = YOLO(MODEL)
    return _model


def names() -> dict[int, str]:
    """Label id to name, one-based to keep zero for unlabelled."""
    return {index + 1: name for index, name in _load().names.items()}


def label_image(rgb: np.ndarray) -> np.ndarray:
    """Per-pixel label ids for one image, zero where nothing was recognised.

    Instances are painted most-confident last, so where two overlap the more certain one
    wins the shared pixels.
    """
    model = _load()
    result = model.predict(rgb[:, :, ::-1], verbose=False, device=0,
                           conf=CONFIDENCE, retina_masks=True)[0]

    labels = np.zeros(rgb.shape[:2], dtype=np.uint8)
    if result.masks is None:
        return labels

    masks = result.masks.data.cpu().numpy() > 0.5
    classes = result.boxes.cls.cpu().numpy().astype(int)
    scores = result.boxes.conf.cpu().numpy()

    for index in np.argsort(scores):
        labels[masks[index]] = classes[index] + 1
    return labels
