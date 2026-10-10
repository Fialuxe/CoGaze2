"""WebRTC signaling relay between the expert and the worker (#19).

The two Unity apps find each other here and exchange SDP offers/answers and ICE candidates; the
media and data channels then run peer to peer and never touch this server.

    ws://host:port/signal/expert     the expert PC
    ws://host:port/signal/worker     the Quest

One peer per role. A new connection for a role replaces the old one (a reconnecting client must not
be locked out by its own half-dead socket). Text messages from one peer are forwarded verbatim to
the other; the relay does not parse them, so the Unity side owns the message format
(`Transport.WebRtc/Pure/SignalMessage.cs`). The relay adds one message of its own, sent to both
peers whenever the other side arrives or leaves:

    {"type": "peer", "present": true | false}

The worker makes the offer when it sees the expert present; either side tears its peer connection
down when told the other has left. Messages sent while the other side is absent are dropped —
negotiation restarts from the next "peer present" anyway.

Runs inside service.py (same port as the reconstruction routes) or on its own when the expert PC
has no GPU stack:

    python signaling.py [--host 0.0.0.0] [--port 8765]
"""

from __future__ import annotations

import argparse
import json

from aiohttp import WSMsgType, web

ROLES = ("expert", "worker")
PEERS = web.AppKey("signal_peers", dict)


def _other(role: str) -> str:
    return "worker" if role == "expert" else "expert"


def _presence(present: bool) -> str:
    return json.dumps({"type": "peer", "present": present})


async def _send(ws: web.WebSocketResponse | None, text: str) -> None:
    if ws is None or ws.closed:
        return
    try:
        await ws.send_str(text)
    except ConnectionError:
        pass


async def signal_handler(request: web.Request) -> web.WebSocketResponse:
    role = request.match_info["role"]
    if role not in ROLES:
        raise web.HTTPNotFound(text=f"unknown role {role!r}; use one of {ROLES}")

    ws = web.WebSocketResponse(heartbeat=5.0)
    await ws.prepare(request)
    peers = request.app[PEERS]

    old = peers.get(role)
    peers[role] = ws
    if old is not None and not old.closed:
        await old.close(code=4000, message=b"replaced by a new connection")
    print(f"[signal] {role} connected{' (replaced the previous one)' if old is not None else ''}")

    other = peers.get(_other(role))
    await _send(ws, _presence(other is not None and not other.closed))
    await _send(other, _presence(True))

    try:
        async for msg in ws:
            if msg.type == WSMsgType.TEXT:
                await _send(peers.get(_other(role)), msg.data)
    finally:
        if peers.get(role) is ws:          # not replaced meanwhile
            del peers[role]
            await _send(peers.get(_other(role)), _presence(False))
            print(f"[signal] {role} disconnected")
    return ws


def add_routes(app: web.Application) -> None:
    """Adds /signal/{role} to an existing aiohttp app (service.py) or a standalone one."""
    app[PEERS] = {}
    app.add_routes([web.get("/signal/{role}", signal_handler)])


def make_app() -> web.Application:
    app = web.Application()
    add_routes(app)
    return app


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    print(f"[signal] relay on ws://{args.host}:{args.port}/signal/{{expert,worker}}")
    web.run_app(make_app(), host=args.host, port=args.port, print=None)


if __name__ == "__main__":
    main()
