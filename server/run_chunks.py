"""Replays a recorded session into chunk-update files — the staging step before streaming.

    python run_chunks.py <session_dir> [--out chunks] [--estimator foundation] [--limit N]

Writes one ``<index>.cgzm`` per keyframe that changed anything: the exact bytes
``service.py`` will one day push over the socket (``chunks.Update.encode``), but to disk.
The expert client reads them in order, so whether Unity renders our geometry in the right
place — the Y flip and the Draco path especially — can be settled before any transport
exists. Once the geometry appears where it should, disk is swapped for a WebSocket and
nothing else about this changes.

Depth is estimated once per keyframe up front, as in ``run_fusion``, so the expensive step
runs a single time. Integration and chunking then run per keyframe, so each file holds only
what that keyframe touched — a genuine incremental stream, not the whole map re-sent.

Semantics are not run here: labels ride in the wire format as one zero byte a vertex, which
still exercises the client's parsing. Person masking and real labels belong to the step that
folds semantics into integration, not to this one.
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import fusion
import keyframes
import rectify
from chunks import Update, build
from depth import estimator


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("session", type=Path)
    parser.add_argument("--out", type=Path, default=Path("chunks"))
    parser.add_argument("--estimator", default="foundation")
    parser.add_argument("--limit", type=int, default=0, help="use only the first N keyframes")
    parser.add_argument("--decay", type=float, default=1.0,
                        help="per-keyframe weight decay; below 1 lets moving objects fade")
    args = parser.parse_args()

    frames = keyframes.load_session(args.session)
    if args.limit:
        frames = frames[:args.limit]
    print(f"{len(frames)} keyframes, baseline {frames[0].baseline * 100:.2f} cm")

    # The rig is rigid, so rectification is computed once for the whole session.
    rectification = rectify.build(frames[0])
    estimate = estimator(args.estimator)

    started = time.time()
    results, colours, weights = [], [], []
    for n, frame in enumerate(frames):
        left, right, camera = rectification.apply(frame)
        result = estimate(left, right, camera, rectification.baseline)
        results.append(result)
        colours.append(left)
        weights.append(result.confidence * fusion.incidence_weight(result))
        print(f"  depth {frame.index:05d}  coverage {result.coverage * 100:5.1f}%  "
              f"({n + 1}/{len(frames)}, {time.time() - started:.0f}s)")

    low, high = fusion.bounds_from(frames, results)
    print(f"\nvolume {high[0] - low[0]:.2f} x {high[1] - low[1]:.2f} x {high[2] - low[2]:.2f} m")

    args.out.mkdir(parents=True, exist_ok=True)

    def fresh_grids():
        return (fusion.Grid(fusion.NEAR, low, high), fusion.Grid(fusion.FAR, low, high))

    near, far = fresh_grids()
    epoch = frames[0].epoch
    total_bytes = 0
    written = 0

    print()
    for frame, result, colour, weight in zip(frames, results, colours, weights):
        # A new epoch means tracking relocated, so everything before it is in a different
        # frame. Start both grids over; the client, seeing the epoch advance, clears too.
        if frame.epoch != epoch:
            epoch = frame.epoch
            near, far = fresh_grids()
            print(f"  epoch -> {epoch}, grids reset")

        if args.decay < 1.0:
            near.decay(args.decay)
            far.decay(args.decay)

        near.integrate(result, colour, weight)
        far.integrate(result, colour, weight)

        near_update = build(near, near.take_dirty(), epoch=frame.epoch, lod="near")
        far_update = build(far, far.take_dirty(), epoch=frame.epoch, lod="far")
        update = Update(epoch=frame.epoch,
                        replaced=near_update.replaced + far_update.replaced,
                        removed=near_update.removed + far_update.removed)
        if not update.replaced and not update.removed:
            continue

        payload = update.encode()
        (args.out / f"{frame.index:05d}.cgzm").write_bytes(payload)
        total_bytes += len(payload)
        written += 1
        print(f"  {frame.index:05d}  "
              f"near +{len(near_update.replaced):3d} -{len(near_update.removed):2d}  "
              f"far +{len(far_update.replaced):3d} -{len(far_update.removed):2d}  "
              f"{len(payload) / 1024:6.1f} KB")

    print(f"\n{written} updates, {total_bytes / 1024:.0f} KB total, written to {args.out}")


if __name__ == "__main__":
    main()
