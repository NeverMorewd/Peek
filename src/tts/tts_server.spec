from PyInstaller.utils.hooks import collect_data_files, collect_submodules

a = Analysis(
    ["tts_server.py"],
    datas=collect_data_files("edge_tts"),
    hiddenimports=[
        *collect_submodules("edge_tts"),
        "certifi",
        "websockets",
        "websockets.legacy",
        "websockets.legacy.client",
    ],
    excludes=[
    "tkinter", "matplotlib", "numpy", "pandas",
    "scipy", "PIL", "cv2", "PyQt5", "PyQt6",
    "pydoc", "doctest", "difflib"
    ],
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,   
    a.datas,      
    [],
    name="tts_server",
    debug=False,
    bootloader_ignore_signals=False,
    strip=True,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,   # onefile marker
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    optimize=2,
)
