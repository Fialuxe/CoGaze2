"""Replays a recorded session into a running service.py as if it were the headset.

    python replay_worker.py [session] [--host localhost] [--port 8765] [--limit N] [--interval 0.3]

Lets the live path be exercised without a Quest: run service.py, connect a Unity expert
(MeshReceiver with serverUrl set), then run this. It sends the same keyframe messages
KeyframeUploader would — recording-format JSON plus two JPEGs — so the server cannot tell it
from the real headset. The mesh appears on the expert as chunks arrive.

This only stands in for the worker's camera and network; the on-device capture path
(KeyframeSource, real passthrough images, WiFi) still needs a real headset to test.
"""

from __future__ import annotations

import argparse
import asyncio
import glob
import json
import struct
from pathlib import Path

import aiohttp
import cv2
import numpy as np

MAGIC, VERSION = b"CGZK", 1


def message(json_path: str) -> bytes:
    """Packs one recorded keyframe into a worker message: header, metadata, then the two JPEGs."""
    meta_bytes = Path(json_path).read_bytes()
    meta = json.loads(meta_bytes.decode("utf-8-sig"))
    height, width, stem = meta["height"], meta["width"], json_path[:-5]

    def jpeg(suffix: str) -> bytes:
        rgb = np.fromfile(f"{stem}_{suffix}.raw", np.uint8).reshape(height, width, 3)
        ok, buffer = cv2.imencode(".jpg", cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR),
                                  [cv2.IMWRITE_JPEG_QUALITY, 95])
        return buffer.tobytes()

    left, right = jpeg("L"), jpeg("R")
    return (struct.pack("<4sHI", MAGIC, VERSION, len(meta_bytes)) + meta_bytes
            + struct.pack("<I", len(left)) + left
            + struct.pack("<I", len(right)) + right)


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", nargs="?", default="../recordings/session01", type=Path)
    parser.add_argument("--host", default="localhost")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--limit", type=int, default=0, help="send only the first N keyframes")
    parser.add_argument("--interval", type=float, default=0.3,
                        help="seconds between sends; the real worker fires at roughly this rate")
    args = parser.parse_args()

    files = sorted(glob.glob(str(args.session / "*.json")))
    if args.limit:
        files = files[:args.limit]
    url = f"ws://{args.host}:{args.port}/worker"

    async with aiohttp.ClientSession() as session:
        async with session.ws_connect(url, max_msg_size=64 * 1024 * 1024) as ws:
            print(f"connected to {url}, sending {len(files)} keyframes")
            for path in files:
                await ws.send_bytes(message(path))
                print(f"  sent {Path(path).stem}")
                await asyncio.sleep(args.interval)
    print("done - the server keeps meshing after the last send; watch the expert")


if __name__ == "__main__":
    asyncio.run(main())
