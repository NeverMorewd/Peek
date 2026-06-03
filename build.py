#!/usr/bin/env python3

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent.resolve()
SRC = ROOT / "src"

CLIENT_DIR = SRC / "client"
TTS_DIR    = SRC / "tts"
WORKER_DIR = SRC / "worker"

DIST = ROOT / "dist" / "win-x64"


def run(cmd: list[str], cwd: Path | None = None) -> None:
    print(f"  $ {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=str(cwd) if cwd else None)
    if result.returncode != 0:
        print(f"\n[ERROR] Command failed with exit code {result.returncode}")
        sys.exit(result.returncode)


def clean() -> None:
    if DIST.exists():
        print(f"  Removing {DIST}")
        shutil.rmtree(DIST)
    DIST.mkdir(parents=True, exist_ok=True)


# ---------------------------------------------------------------------------
# C# AOT
# ---------------------------------------------------------------------------

def build_csharp() -> None:
    slnx = next(CLIENT_DIR.glob("*.slnx"), None) or next(CLIENT_DIR.glob("*.sln"), None)
    if slnx is None:
        print("[ERROR] No .slnx / .sln found in src/client")
        sys.exit(1)

    run([
        "dotnet", "publish", str(slnx),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishAot=true",
        "-p:StripSymbols=true",
        "-o", str(DIST),
    ], cwd=CLIENT_DIR)
    libvlc = DIST / "libvlc"
    if libvlc.exists():
        for d in libvlc.iterdir():
            if d.is_dir() and d.name != "win-x64":
                print(f"  Removing libvlc/{d.name}")
                shutil.rmtree(d)
    pdbs = list(DIST.rglob("*.pdb"))
    for pdb in pdbs:
        pdb.unlink()
    if pdbs:
        print(f"  Removed {len(pdbs)} .pdb file(s)")
    print(f"  C# artifacts -> {DIST.relative_to(ROOT)}")


# ---------------------------------------------------------------------------
# Python (PyInstaller via tts_server.spec)
# ---------------------------------------------------------------------------

def build_python() -> None:
    spec = TTS_DIR / "tts_server.spec"
    if not spec.exists():
        print(f"[ERROR] Spec file not found: {spec}")
        sys.exit(1)

    packages = ["edge-tts", "fastapi", "uvicorn", "pyinstaller"]
    run([sys.executable, "-m", "pip", "install", "--upgrade", "--quiet"] + packages)

    tts_dist  = TTS_DIR / "dist"
    tts_build = TTS_DIR / "build"

    run([
        sys.executable, "-m", "PyInstaller",
        str(spec),
        "--noconfirm",
        "--distpath", str(tts_dist),
        "--workpath", str(tts_build),
    ], cwd=TTS_DIR)

    src_folder = tts_dist / "tts_server"
    src_single = tts_dist / "tts_server_onefile.exe"

    if src_folder.exists():
        shutil.copytree(src_folder, DIST, dirs_exist_ok=True)
    elif src_single.exists():
        shutil.copy2(src_single, DIST / "tts_server.exe")
    else:
        print("[ERROR] PyInstaller produced no recognisable output")
        sys.exit(1)

    print(f"  Python artifacts -> {DIST.relative_to(ROOT)}")


# ---------------------------------------------------------------------------
# Rust (cargo build --release)
# ---------------------------------------------------------------------------

def build_rust() -> None:
    cargo_toml = WORKER_DIR / "Cargo.toml"
    if not cargo_toml.exists():
        print(f"[ERROR] Cargo.toml not found in src/worker")
        sys.exit(1)

    run([
        "cargo", "build",
        "--release",
        "--target", "x86_64-pc-windows-msvc",
    ], cwd=WORKER_DIR)

    release_dir = WORKER_DIR / "target" / "x86_64-pc-windows-msvc" / "release"
    exes = [p for p in release_dir.glob("*.exe") if not p.name.startswith(".")]

    if not exes:
        print("[ERROR] No .exe found in cargo release output")
        sys.exit(1)

    for exe in exes:
        shutil.copy2(exe, DIST / exe.name)
        print(f"  Rust artifact -> {(DIST / exe.name).relative_to(ROOT)}")


# ---------------------------------------------------------------------------
# Verify
# ---------------------------------------------------------------------------

def verify() -> None:
    print(f"\n[Build outputs]  {DIST.relative_to(ROOT)}")
    for p in sorted(DIST.rglob("*")):
        if p.is_file():
            size_mb = p.stat().st_size / 1_048_576
            print(f"  {str(p.relative_to(DIST)):<55}  {size_mb:>7.1f} MB")


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

STEPS = ("csharp", "python", "rust")

def main() -> None:
    parser = argparse.ArgumentParser(description="Peek – unified build script")
    parser.add_argument(
        "--only",
        nargs="+",
        choices=STEPS,
        metavar="STEP",
        help=f"Build only specific steps: {', '.join(STEPS)}",
    )
    parser.add_argument("--clean-only", action="store_true")
    args = parser.parse_args()

    steps = args.only or list(STEPS)

    print("=" * 60)
    print(" Peek – Unified Build")
    print(f" Steps: {', '.join(steps)}")
    print("=" * 60)

    print("\n[0] Preparing output directory ...")
    clean()

    if args.clean_only:
        print("Clean done.")
        return

    builders = {
        "csharp": ("C# AOT",     build_csharp),
        "python": ("Python/TTS", build_python),
        "rust":   ("Rust worker", build_rust),
    }

    for i, step in enumerate(steps, 1):
        label, fn = builders[step]
        print(f"\n[{i}/{len(steps)}] Building {label} ...")
        fn()

    verify()

    print("\nDone!")
    print("=" * 60)


if __name__ == "__main__":
    main()