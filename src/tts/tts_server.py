
import argparse
import asyncio
import io
import os
import sys
import tempfile
import uuid

import edge_tts
import uvicorn
from fastapi import FastAPI, Query, HTTPException
from fastapi.responses import JSONResponse, StreamingResponse, FileResponse


app = FastAPI(title="EdgeTTS Service", version="1.0.0", docs_url=None, redoc_url=None)


@app.get("/health")
async def health():
    """Liveness probe – C# polls this after spawning the process."""
    return {"status": "ok", "version": "1.0.0"}


@app.get("/voices")
async def list_voices():
    """
    Return all voices supported by the Edge TTS service.
    Response is a JSON array of voice objects, each containing:
      Name, ShortName, Gender, Locale, SuggestedCodec, FriendlyName, Status
    """
    try:
        voices = await edge_tts.list_voices()
        return JSONResponse(content=voices)
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"Failed to fetch voices: {exc}") from exc


@app.get("/speak")
async def speak(
    text: str = Query(..., description="Text to synthesize"),
    voice: str = Query("zh-CN-XiaoxiaoNeural", description="Edge TTS voice short name"),
    rate: str = Query("+0%", description="Speech rate, e.g. +20% or -10%"),
    volume: str = Query("+0%", description="Volume, e.g. +0% to +100%"),
    pitch: str = Query("+0Hz", description="Pitch offset, e.g. +50Hz or -20Hz"),
):
    """
    Synthesize *text* and stream the resulting MP3 back to the caller.
    The C# client can download the bytes and write them to a temp file for playback.
    """
    if not text.strip():
        raise HTTPException(status_code=400, detail="text must not be empty")

    try:
        communicate = edge_tts.Communicate(
            text,
            voice,
            rate=rate,
            volume=volume,
            pitch=pitch,
        )

        # Collect all audio chunks into a buffer so we can stream cleanly
        buffer = io.BytesIO()
        async for chunk in communicate.stream():
            if chunk["type"] == "audio":
                buffer.write(chunk["data"])

        if buffer.tell() == 0:
            raise HTTPException(status_code=502, detail="Edge TTS returned no audio data")

        buffer.seek(0)
        return StreamingResponse(buffer, media_type="audio/mpeg")

    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"TTS synthesis failed: {exc}") from exc


@app.get("/speak/save")
async def speak_save(
    text: str = Query(..., description="Text to synthesize"),
    voice: str = Query("zh-CN-XiaoxiaoNeural", description="Edge TTS voice short name"),
    rate: str = Query("+0%"),
    volume: str = Query("+0%"),
    pitch: str = Query("+0Hz"),
):
    """
    Synthesize *text*, save to a temp MP3 file, and return the file path.
    Useful when the C# host prefers to play a local file directly.
    The caller is responsible for deleting the file after playback.
    """
    if not text.strip():
        raise HTTPException(status_code=400, detail="text must not be empty")

    try:
        communicate = edge_tts.Communicate(text, voice, rate=rate, volume=volume, pitch=pitch)

        tmp_path = os.path.join(tempfile.gettempdir(), f"tts_{uuid.uuid4().hex}.mp3")
        await communicate.save(tmp_path)

        if not os.path.exists(tmp_path) or os.path.getsize(tmp_path) == 0:
            raise HTTPException(status_code=502, detail="Edge TTS produced an empty file")

        return JSONResponse({"path": tmp_path})

    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"TTS synthesis failed: {exc}") from exc


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="EdgeTTS HTTP microservice")
    parser.add_argument("--host", default="127.0.0.1", help="Bind host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=5050, help="Bind port (default: 5050)")
    return parser.parse_args()


if __name__ == "__main__":
    args = _parse_args()
    uvicorn.run(
        app,
        host=args.host,
        port=args.port,
        log_level="warning",
        access_log=False,
    )
