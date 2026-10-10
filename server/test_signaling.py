"""Self-check for the signaling relay: two clients exchange an offer, an answer and candidates.

    python test_signaling.py        (or: pytest test_signaling.py)

Starts the relay on a free local port, connects a fake expert and a fake worker, and checks that
presence is announced, messages cross verbatim in both directions, a reconnecting role replaces its
old socket, and a leaving peer is reported. Needs only aiohttp.
"""

from __future__ import annotations

import asyncio
import json

import aiohttp
from aiohttp import web

import signaling


async def _next_json(ws: aiohttp.ClientWebSocketResponse) -> dict:
    msg = await asyncio.wait_for(ws.receive(), timeout=5)
    assert msg.type == aiohttp.WSMsgType.TEXT, msg
    return json.loads(msg.data)


async def _run() -> None:
    runner = web.AppRunner(signaling.make_app())
    await runner.setup()
    site = web.TCPSite(runner, "127.0.0.1", 0)
    await site.start()
    port = site._server.sockets[0].getsockname()[1]
    base = f"ws://127.0.0.1:{port}/signal"

    async with aiohttp.ClientSession() as http:
        expert = await http.ws_connect(f"{base}/expert")
        assert await _next_json(expert) == {"type": "peer", "present": False}

        worker = await http.ws_connect(f"{base}/worker")
        assert await _next_json(worker) == {"type": "peer", "present": True}
        assert await _next_json(expert) == {"type": "peer", "present": True}

        offer = {"type": "offer", "sdp": "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\n"}
        await worker.send_str(json.dumps(offer))
        assert await _next_json(expert) == offer

        answer = {"type": "answer", "sdp": "v=0\r\n"}
        await expert.send_str(json.dumps(answer))
        assert await _next_json(worker) == answer

        for side, other in ((worker, expert), (expert, worker)):
            cand = {"type": "candidate", "candidate": "candidate:1 1 udp 2122260223 192.168.0.2 50000 typ host",
                    "sdpMid": "0", "sdpMLineIndex": 0}
            await side.send_str(json.dumps(cand))
            assert await _next_json(other) == cand

        # A reconnecting expert replaces its old socket; the worker sees it arrive again.
        expert2 = await http.ws_connect(f"{base}/expert")
        assert await _next_json(expert2) == {"type": "peer", "present": True}
        assert await _next_json(worker) == {"type": "peer", "present": True}
        closed = await asyncio.wait_for(expert.receive(), timeout=5)
        assert closed.type in (aiohttp.WSMsgType.CLOSE, aiohttp.WSMsgType.CLOSED), closed

        await worker.send_str(json.dumps(offer))
        assert await _next_json(expert2) == offer

        await worker.close()
        assert await _next_json(expert2) == {"type": "peer", "present": False}
        await expert2.close()

    await runner.cleanup()


def test_relay() -> None:
    asyncio.run(_run())


if __name__ == "__main__":
    test_relay()
    print("signaling relay: OK")
