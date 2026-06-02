#!/usr/bin/env python3
"""
build.py
--------
Cross-platform build helper for tts_server.exe.
Equivalent to build.bat but works on macOS/Linux CI as well.

Usage:
    python build.py              # full build
    python build.py --no-onefile # skip single-file variant (faster)
    python build.py --clean-only # only clean artifacts
"""

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent.resolve()
DIST = ROOT / "dist"
BUILD = ROOT / "build"


def run(cmd: list[str], **kwargs) -> None:
    """Run a subprocess command, exit on failure."""
    print(f"  $ {' '.join(cmd)}")
    result = subprocess.run(cmd, **kwargs)
    if result.returncode != 0:
        print(f"\n[ERROR] Command failed with exit code {result.returncode}")
        sys.exit(result.returncode)


def clean() -> None:
    """Remove previous build artifacts."""
    for d in (DIST, BUILD):
        if d.exists():
            print(f"  Removing {d}")
            shutil.rmtree(d)

    # Remove stray __pycache__ dirs
    for p in ROOT.rglob("__pycache__"):
        shutil.rmtree(p, ignore_errors=True)


def install_deps() -> None:
    """Install / upgrade all required packages."""
    packages = ["edge-tts", "fastapi", "uvicorn", "pyinstaller"]
    run([sys.executable, "-m", "pip", "install", "--upgrade", "--quiet"] + packages)


def build(no_onefile: bool = False) -> None:
    """Invoke PyInstaller with the project spec file."""
    spec = ROOT / "tts_server.spec"
    if not spec.exists():
        print(f"[ERROR] Spec file not found: {spec}")
        sys.exit(1)

    cmd = [
        sys.executable, "-m", "PyInstaller",
        str(spec),
        "--noconfirm",
    ]
    run(cmd, cwd=str(ROOT))


def verify() -> None:
    """Print a summary of build outputs."""
    print("\n[Build outputs]")
    folder_exe = DIST / "tts_server" / "tts_server.exe"
    single_exe = DIST / "tts_server_onefile.exe"

    for p in (folder_exe, single_exe):
        if p.exists():
            size_mb = p.stat().st_size / 1_048_576
            print(f"  OK  {p.relative_to(ROOT)}  ({size_mb:.1f} MB)")
        else:
            print(f"  --  {p.relative_to(ROOT)}  (not built)")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build tts_server.exe")
    parser.add_argument("--no-onefile", action="store_true", help="Skip single-file build")
    parser.add_argument("--clean-only", action="store_true", help="Only clean, do not build")
    args = parser.parse_args()

    print("=" * 60)
    print(" EdgeTTS Server – Python Build Script")
    print("=" * 60)

    print("\n[1/4] Cleaning previous artifacts ...")
    clean()

    if args.clean_only:
        print("Clean done.")
        return

    print("\n[2/4] Installing dependencies ...")
    install_deps()

    print("\n[3/4] Running PyInstaller ...")
    build(no_onefile=args.no_onefile)

    print("\n[4/4] Verifying outputs ...")
    verify()

    print("\nDone! Copy dist/tts_server/ into your C# project output directory.")
    print("=" * 60)


if __name__ == "__main__":
    main()
