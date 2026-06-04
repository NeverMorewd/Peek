import argparse
import asyncio
import io
import json
import os
import sys
import tempfile
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs
from threading import Thread

import edge_tts


def run_async(coro):
    return asyncio.run(coro)


class TTSHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def _send(self, status: int, content_type: str, body: bytes):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _json(self, status: int, data):
        self._send(status, "application/json", json.dumps(data).encode())

    def do_GET(self):
        parsed = urlparse(self.path)
        qs = parse_qs(parsed.query)
        p = parsed.path

        if p == "/health":
            self._json(200, {"status": "ok", "version": "1.0.0"})

        elif p == "/voices":
            try:
                voices = run_async(edge_tts.list_voices())
                self._json(200, voices)
            except Exception as e:
                self._json(502, {"detail": str(e)})

        elif p == "/speak":
            self._handle_speak(qs, stream=True)

        elif p == "/speak/save":
            self._handle_speak(qs, stream=False)

        else:
            self._json(404, {"detail": "not found"})

    def _handle_speak(self, qs, stream: bool):
        text   = qs.get("text",   [""])[0].strip()
        voice  = qs.get("voice",  ["zh-CN-XiaoxiaoNeural"])[0]
        rate   = qs.get("rate",   ["+0%"])[0]
        volume = qs.get("volume", ["+0%"])[0]
        pitch  = qs.get("pitch",  ["+0Hz"])[0]

        if not text:
            self._json(400, {"detail": "text must not be empty"})
            return

        try:
            comm = edge_tts.Communicate(text, voice, rate=rate, volume=volume, pitch=pitch)

            if stream:
                buf = io.BytesIO()
                async def collect():
                    async for chunk in comm.stream():
                        if chunk["type"] == "audio":
                            buf.write(chunk["data"])
                run_async(collect())
                if buf.tell() == 0:
                    self._json(502, {"detail": "no audio data"})
                    return
                buf.seek(0)
                self._send(200, "audio/mpeg", buf.read())
            else:
                tmp = os.path.join(tempfile.gettempdir(), f"tts_{uuid.uuid4().hex}.mp3")
                run_async(comm.save(tmp))
                if not os.path.exists(tmp) or os.path.getsize(tmp) == 0:
                    self._json(502, {"detail": "empty file"})
                    return
                self._json(200, {"path": tmp})

        except Exception as e:
            self._json(502, {"detail": str(e)})


if __name__ == "__main__":
    import traceback
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5050)
    args = parser.parse_args()

    try:
        print(f"Starting on {args.host}:{args.port}", flush=True)
        server = HTTPServer((args.host, args.port), TTSHandler)
        print("Server ready", flush=True)
        server.serve_forever()
    except Exception:
        traceback.print_exc()
        sys.exit(1)