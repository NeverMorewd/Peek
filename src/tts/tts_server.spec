# tts_server.spec
# PyInstaller spec file for tts_server.py
#
# Usage:
#   pyinstaller tts_server.spec
#
# Output:
#   dist/tts_server/tts_server.exe  (one-folder mode, faster startup)
#   dist/tts_server_onefile/tts_server.exe  (single-file mode, easier to ship)

import sys
from PyInstaller.utils.hooks import collect_data_files, collect_submodules

# ---------------------------------------------------------------------------
# Collect edge_tts data files (trustedlist, voices metadata, etc.)
# ---------------------------------------------------------------------------
edge_tts_datas = collect_data_files("edge_tts")

# ---------------------------------------------------------------------------
# Hidden imports that PyInstaller misses via static analysis
# ---------------------------------------------------------------------------
hidden = (
    collect_submodules("edge_tts")
    + collect_submodules("uvicorn")
    + collect_submodules("fastapi")
    + collect_submodules("anyio")
    + collect_submodules("anyio._backends")
    + collect_submodules("starlette")
    + [
        "uvicorn.logging",
        "uvicorn.loops",
        "uvicorn.loops.asyncio",
        "uvicorn.loops.uvloop",
        "uvicorn.protocols",
        "uvicorn.protocols.http",
        "uvicorn.protocols.http.auto",
        "uvicorn.protocols.http.h11_impl",
        "uvicorn.protocols.http.httptools_impl",
        "uvicorn.protocols.websockets",
        "uvicorn.protocols.websockets.auto",
        "uvicorn.protocols.websockets.websockets_impl",
        "uvicorn.protocols.websockets.wsproto_impl",
        "uvicorn.lifespan",
        "uvicorn.lifespan.off",
        "uvicorn.lifespan.on",
        "anyio",
        "anyio._backends._asyncio",
        "certifi",
        "aiohttp",
        "aiohttp.connector",
    ]
)

# ---------------------------------------------------------------------------
# Analysis
# ---------------------------------------------------------------------------
a = Analysis(
    ["tts_server.py"],
    pathex=[],
    binaries=[],
    datas=edge_tts_datas,
    hiddenimports=hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        "tkinter",
        "matplotlib",
        "numpy",
        "pandas",
        "scipy",
        "PIL",
        "cv2",
        "PyQt5",
        "PyQt6",
        "wx",
    ],
    noarchive=False,
    optimize=1,
)

pyz = PYZ(a.pure)

# ---------------------------------------------------------------------------
# One-folder EXE (recommended: faster cold start, easier to debug)
# ---------------------------------------------------------------------------
exe_folder = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="tts_server",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,          # keep console so C# can read stderr if needed
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

coll = COLLECT(
    exe_folder,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="tts_server",     # output folder: dist/tts_server/
)

# ---------------------------------------------------------------------------
# Single-file EXE (optional; slower first launch due to extraction overhead)
# ---------------------------------------------------------------------------
exe_onefile = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="tts_server_onefile",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
