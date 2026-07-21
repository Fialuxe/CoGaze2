"""Fast-FoundationStereo (NVLabs, CVPR 2026).

A distilled, real-time variant of FoundationStereo — over ten times faster at close to the same
zero-shot accuracy, so depth can keep up while the worker works. It carries the same monocular
priors, so it keeps the coverage on textureless surfaces that is the whole reason a learned
matcher is used here.

Same inputs and outputs as the ``foundation`` estimator, so switching between them is a change of
name. Two things differ under the hood: the checkpoint is a serialized model object rather than a
state dict, and the forward takes an extra build-volume flag.

Weights are NVIDIA's under a non-commercial licence, so this is for research use. The 20-26-39
checkpoint is the fastest of the family — ~44 ms at 640x480 on a 3090 at eight iterations.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import torch

from depth import DepthResult
from keyframes import Camera

_REPO = Path(__file__).resolve().parents[2] / "third_party" / "Fast-FoundationStereo"
_CHECKPOINT = _REPO / "weights" / "20-26-39" / "model_best_bp2_serialize.pth"

# Eight is the demo default and the setting the runtime table is quoted at; fewer trades a little
# accuracy for speed. The refinement iterations matter more here than for FoundationStereo.
VALID_ITERS = 8
MAX_DISP = 192
_AMP_DTYPE = torch.float16

# The fast build-volume kernels are wrapped in torch.compile, which needs Triton to generate GPU
# code. Where Triton is present (Linux by default, or triton-windows) they compile and run several
# times faster; where it is absent, dynamo is disabled so the same ops run eagerly — slower, but
# still well ahead of FoundationStereo and requiring nothing. The code adapts to the machine, so no
# environment needs a different build.
try:
    import triton  # noqa: F401
    _HAS_TRITON = True
except ImportError:
    _HAS_TRITON = False
    import torch._dynamo
    torch._dynamo.config.disable = True

_model = None
_padder_cls = None


def _load():
    """Loads the serialized model once. It is pickled as a whole object, so the repo's class
    definitions have to be importable — which the path insert arranges."""
    global _model, _padder_cls
    if _model is not None:
        return _model

    sys.path.insert(0, str(_REPO))
    from core.utils.utils import InputPadder

    # The live stream is a fixed image size, so let cuDNN autotune its convolutions once.
    torch.backends.cudnn.benchmark = True

    torch.autograd.set_grad_enabled(False)
    model = torch.load(_CHECKPOINT, map_location="cpu", weights_only=False)
    model.args.valid_iters = VALID_ITERS
    model.args.max_disp = MAX_DISP
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

    with torch.amp.autocast("cuda", enabled=True, dtype=_AMP_DTYPE):
        disparity = model.forward(left_t, right_t, iters=VALID_ITERS, test_mode=True,
                                  optimize_build_volume="pytorch1")
    disparity = padder.unpad(disparity.float()).cpu().numpy().reshape(height, width)

    # The model emits a value everywhere, including where the matching pixel lies outside the
    # right image. Those have no supporting evidence, so they are dropped.
    columns = np.arange(width)[None, :]
    valid = (disparity > 0) & (columns - disparity >= 0)

    depth = np.full(disparity.shape, np.inf, dtype=np.float32)
    np.divide(camera.fx * baseline, disparity, out=depth, where=valid)

    confidence = valid.astype(np.float32)
    return DepthResult(depth=depth, confidence=confidence, camera=camera)
