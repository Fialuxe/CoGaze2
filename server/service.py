"""The reconstruction service.

Turns the batch pipeline into a live loop. The worker streams posed stereo keyframes in over
one WebSocket; the server fuses them and streams the mesh chunks that changed out over another.
The computation is the batch pipeline unchanged (rectify -> depth -> integrate -> chunk); what
is added here is the loop that holds the grids across frames, the two sockets, and the log.

    python service.py [--host 0.0.0.0] [--port 8765] [--estimator foundation] [--log-dir logs/live]

Two roles on one server, told apart by path:

  ws://host:port/worker   the headset connects and sends keyframe messages (see below)
  ws://host:port/expert   the expert client connects and receives chunk updates (chunks.Update)
  ws://host:port/signal/{expert,worker}   WebRTC signaling relay for the 2D video path (signaling.py)

Every keyframe is written to the log before anything else. The TSDF lives only in GPU memory,
so without this a crash costs the whole session; and the log is the recording format, so a live
session reprocesses exactly like a recorded one — which is how every estimator comparison so far
was made.

Bounds are derived from the first keyframe of an epoch and then fixed, because a streaming server
cannot see the whole session up front. Geometry the worker later moves toward, beyond that box, is
clipped: a growing grid that follows the worker is the documented next step, not this one.

Worker keyframe message (one binary frame):

    <4s H I>  magic "CGZK", version, json length
    [json]    the recording-format metadata (index, epoch, width, height, left/right intrinsics
              and world_T_camera) — logged verbatim, so the log is a recording
    <I>[jpg]  left  image, JPEG
    <I>[jpg]  right image, JPEG
"""

from __future__ import annotations

import argparse
import asyncio
import struct
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import cv2
import numpy as np
from aiohttp import WSMsgType, web

import chunks
import fusion
import keyframes
import rectify
import signaling
from depth import estimator

KF_MAGIC = b"CGZK"
KF_VERSION = 1


def decode_keyframe(data: bytes) -> tuple[keyframes.Keyframe, bytes, bytes, bytes]:
    """Parses a worker message into a Keyframe plus the raw metadata and JPEGs kept for the log."""
    magic, version, json_len = struct.unpack_from("<4sHI", data, 0)
    if magic != KF_MAGIC or version != KF_VERSION:
        raise ValueError(f"unexpected keyframe header {magic!r} v{version}")
    off = 10
    meta_bytes = data[off:off + json_len]; off += json_len
    (left_len,) = struct.unpack_from("<I", data, off); off += 4
    left_jpg = data[off:off + left_len]; off += left_len
    (right_len,) = struct.unpack_from("<I", data, off); off += 4
    right_jpg = data[off:off + right_len]

    import json
    meta = json.loads(meta_bytes)

    def to_rgb(jpg: bytes) -> np.ndarray:
        bgr = cv2.imdecode(np.frombuffer(jpg, np.uint8), cv2.IMREAD_COLOR)
        return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

    keyframe = keyframes.Keyframe(
        index=meta["index"], epoch=meta["epoch"],
        width=meta["width"], height=meta["height"],
        left=keyframes._camera(meta["left"]), right=keyframes._camera(meta["right"]),
        left_rgb=to_rgb(left_jpg), right_rgb=to_rgb(right_jpg))
    return keyframe, meta_bytes, left_jpg, right_jpg


class Log:
    """Writes each keyframe to disk in the recording format, so a live session can be replayed."""

    def __init__(self, directory: Path):
        self.directory = directory
        directory.mkdir(parents=True, exist_ok=True)

    def write(self, index: int, meta_bytes: bytes, left_jpg: bytes, right_jpg: bytes) -> None:
        stem = self.directory / f"{index:05d}"
        stem.with_suffix(".json").write_bytes(meta_bytes)
        (self.directory / f"{index:05d}_L.jpg").write_bytes(left_jpg)
        (self.directory / f"{index:05d}_R.jpg").write_bytes(right_jpg)


def _downscale(left: np.ndarray, right: np.ndarray, camera: keyframes.Camera, scale: float):
    """Shrinks the stereo pair and its intrinsics together. Depth cost falls with pixel count, so
    this is the dominant speed knob; measured on this rig, 0.5 keeps coverage and moves the depth
    less than the sensor noise floor (see measure_depth_speed.py)."""
    size = (int(left.shape[1] * scale), int(left.shape[0] * scale))  # (w, h)
    small = keyframes.Camera(fx=camera.fx * scale, fy=camera.fy * scale,
                             cx=camera.cx * scale, cy=camera.cy * scale, pose=camera.pose)
    return (cv2.resize(left, size, interpolation=cv2.INTER_AREA),
            cv2.resize(right, size, interpolation=cv2.INTER_AREA), small)


class Reconstructor:
    """Holds the two grids across frames and turns each keyframe into a chunk update.

    Not thread-safe: the grids are CUDA tensors mutated in place, so keyframes must be processed
    one at a time. The server enforces that with a single-worker executor.
    """

    def __init__(self, estimator_name: str, margin: float = 0.2,
                 scale: float = 1.0, iters: int | None = None):
        self.estimate = estimator(estimator_name)
        self.margin = margin
        self.scale = scale
        # Refinement iterations are a knob on both learned estimators; for FoundationStereo the
        # backbone dominates so resolution matters more, but Fast-FoundationStereo responds to it.
        if iters is not None:
            if estimator_name == "foundation":
                from depth import foundation_stereo
                foundation_stereo.VALID_ITERS = iters
            elif estimator_name == "fast":
                from depth import fast_foundation_stereo
                fast_foundation_stereo.VALID_ITERS = iters
        self.rectification: rectify.Rectification | None = None
        self.near: fusion.Grid | None = None
        self.far: fusion.Grid | None = None
        self.epoch: int | None = None

    def process(self, keyframe: keyframes.Keyframe) -> tuple[chunks.Update, bool]:
        reset = keyframe.epoch != self.epoch
        if reset:
            self.epoch = keyframe.epoch
            self.near = self.far = None          # rebuilt from the new epoch's first bounds

        # The rig is rigid, so rectification is derived once and reused for the whole session.
        if self.rectification is None:
            self.rectification = rectify.build(keyframe)

        left, right, camera = self.rectification.apply(keyframe)
        if self.scale != 1.0:
            left, right, camera = _downscale(left, right, camera, self.scale)
        result = self.estimate(left, right, camera, self.rectification.baseline)
        weight = result.confidence * fusion.incidence_weight(result)

        if self.near is None:
            low, high = fusion.bounds_from([keyframe], [result], margin=self.margin)
            self.near = fusion.Grid(fusion.NEAR, low, high)
            self.far = fusion.Grid(fusion.FAR, low, high)

        self.near.integrate(result, left, weight)
        self.far.integrate(result, left, weight)

        near_update = chunks.build(self.near, self.near.take_dirty(), epoch=keyframe.epoch, lod="near")
        far_update = chunks.build(self.far, self.far.take_dirty(), epoch=keyframe.epoch, lod="far")
        update = chunks.Update(epoch=keyframe.epoch,
                               replaced=near_update.replaced + far_update.replaced,
                               removed=near_update.removed + far_update.removed)
        return update, reset


async def broadcast(app: web.Application, payload: bytes) -> None:
    for ws in list(app["experts"]):
        try:
            await ws.send_bytes(payload)
        except ConnectionError:
            app["experts"].discard(ws)


async def worker_handler(request: web.Request) -> web.WebSocketResponse:
    ws = web.WebSocketResponse(max_msg_size=64 * 1024 * 1024)
    await ws.prepare(request)
    app = request.app
    print("[service] worker connected")

    async for msg in ws:
        if msg.type != WSMsgType.BINARY:
            continue
        keyframe, meta_bytes, left_jpg, right_jpg = decode_keyframe(msg.data)
        app["log"].write(keyframe.index, meta_bytes, left_jpg, right_jpg)

        # Reconstruction is heavy and synchronous; run it off the event loop so the socket stays
        # responsive, on a single worker so the grids are only ever touched by one thread.
        loop = asyncio.get_event_loop()
        update, reset = await loop.run_in_executor(app["pool"], app["recon"].process, keyframe)

        if update.replaced or update.removed or reset:
            await broadcast(app, update.encode())
        print(f"[service] kf {keyframe.index:05d} epoch {keyframe.epoch} "
              f"-> +{len(update.replaced)} -{len(update.removed)}"
              f"{' (reset)' if reset else ''}, {len(app['experts'])} expert(s)")

    print("[service] worker disconnected")
    return ws


async def expert_handler(request: web.Request) -> web.WebSocketResponse:
    ws = web.WebSocketResponse()
    await ws.prepare(request)
    request.app["experts"].add(ws)
    print(f"[service] expert connected ({len(request.app['experts'])} total)")
    try:
        async for _ in ws:          # experts only receive; drain to keep the socket open
            pass
    finally:
        request.app["experts"].discard(ws)
        print("[service] expert disconnected")
    return ws


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--estimator", default="foundation")
    parser.add_argument("--log-dir", type=Path, default=Path("logs/live"))
    parser.add_argument("--margin", type=float, default=0.2,
                        help="metres added around the first keyframe's bounds")
    parser.add_argument("--scale", type=float, default=1.0,
                        help="downscale factor for depth input; 0.5 is ~3x faster at the same coverage")
    parser.add_argument("--iters", type=int, default=None,
                        help="FoundationStereo refinement iterations (default 32); 8 with --scale 0.5 ~1.6 Hz")
    args = parser.parse_args()

    app = web.Application()
    app["experts"] = set()
    app["log"] = Log(args.log_dir)
    app["recon"] = Reconstructor(args.estimator, margin=args.margin,
                                 scale=args.scale, iters=args.iters)
    app["pool"] = ThreadPoolExecutor(max_workers=1)
    app.add_routes([web.get("/worker", worker_handler),
                    web.get("/expert", expert_handler)])
    signaling.add_routes(app)       # /signal/{expert,worker}: WebRTC offer/answer/ICE relay (#19)

    print(f"[service] listening on ws://{args.host}:{args.port}  "
          f"(/worker in, /expert out), logging to {args.log_dir}")
    web.run_app(app, host=args.host, port=args.port, print=None)


if __name__ == "__main__":
    main()
