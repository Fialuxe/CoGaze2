"""FoundationStereo (NVLabs, CVPR 2025).

A learned matcher that carries monocular priors from a vision foundation model into
stereo matching, so it produces depth on surfaces where block matching has nothing to
correlate — plain walls, paper, smooth tabletops. That is the reason for using it here:
the mesh has to be free of holes, and roughly half of a typical desk scene carries too
little local texture for classical matching.

Weights are NVIDIA's under a non-commercial licence, so this is for research use.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import torch

from depth import DepthResult
from keyframes import Camera

_REPO = Path(__file__).resolve().parents[2] / "third_party" / "FoundationStereo"
_CHECKPOINT = _REPO / "pretrained_models" / "23-51-11" / "model_best_bp2.pth"

# Iterative refinement steps. 32 is the demo default; fewer trades accuracy for speed.
VALID_ITERS = 32

_model = None
_padder_cls = None


def _load():
    """Builds the model once. Weights are 3.3 GB, so loading is not something to repeat."""
    global _model, _padder_cls
    if _model is not None:
        return _model

    sys.path.insert(0, str(_REPO))
    from omegaconf import OmegaConf
    from core.foundation_stereo import FoundationStereo
    from core.utils.utils import InputPadder

    cfg = OmegaConf.load(_CHECKPOINT.parent / "cfg.yaml")
    cfg["vit_size"] = cfg.get("vit_size", "vitl")
    # The constructor reads these off the config rather than taking them as arguments.
    cfg["valid_iters"] = VALID_ITERS
    cfg["hiera"] = 0

    torch.autograd.set_grad_enabled(False)
    model = FoundationStereo(OmegaConf.create(cfg))
    model.load_state_dict(torch.load(_CHECKPOINT, weights_only=False)["model"])
    model.cuda().eval()

    _model, _padder_cls = model, InputPadder
    return _model


def estimate(left: np.ndarray, right: np.ndarray, camera: Camera, baseline: float) -> DepthResult:
    model = _load()

    def to_tensor(image: np.ndarray) -> torch.Tensor:
        return torch.as_tensor(image).cuda().float()[None].permute(0, 3, 1, 2)

    height, width = left.shape[:2]
    left_t, right_t = to_tensor(left), to_tensor(right)

    padder = _padder_cls(left_t.shape, divis_by=32, force_square=False)
    left_t, right_t = padder.pad(left_t, right_t)

    with torch.cuda.amp.autocast(True):
        disparity = model.forward(left_t, right_t, iters=VALID_ITERS, test_mode=True)
    disparity = padder.unpad(disparity.float()).cpu().numpy().reshape(height, width)

    # The model emits a value everywhere, including where the matching pixel lies outside
    # the right image. Those have no supporting evidence, so they are dropped.
    columns = np.arange(width)[None, :]
    valid = (disparity > 0) & (columns - disparity >= 0)

    depth = np.full(disparity.shape, np.inf, dtype=np.float32)
    np.divide(camera.fx * baseline, disparity, out=depth, where=valid)

    # No per-pixel confidence is exposed, so this only records where a value survives.
    confidence = valid.astype(np.float32)

    return DepthResult(depth=depth, confidence=confidence, camera=camera)
