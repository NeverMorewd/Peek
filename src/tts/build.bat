@echo off
:: build.bat
:: One-click build script for tts_server.exe on Windows
:: Handles permission-restricted Python installations via --user flag
::
:: Output:
::   dist\tts_server\          <- one-folder build (copy whole folder to C# output)
::   dist\tts_server_onefile.exe <- single-file build

setlocal EnableDelayedExpansion

echo ============================================================
echo  EdgeTTS Server - Build Script
echo ============================================================
echo.

:: ---- Check Python -------------------------------------------------------
where python >nul 2>&1
if errorlevel 1 (
    echo [ERROR] python not found in PATH.
    echo         Install Python 3.9+ from https://python.org and re-run.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('python --version 2^>^&1') do set PY_VER=%%v
echo [OK] Found %PY_VER%

:: ---- Bootstrap pip itself (handles "No module named pip") ---------------
echo.
echo [INFO] Bootstrapping pip ...
python -m ensurepip --upgrade >nul 2>&1
:: If ensurepip is unavailable (some stripped installs), download get-pip.py
python -m pip --version >nul 2>&1
if errorlevel 1 (
    echo [INFO] pip not found, downloading get-pip.py ...
    powershell -NoProfile -ExecutionPolicy Bypass ^
        -Command "Invoke-WebRequest -Uri https://bootstrap.pypa.io/get-pip.py -OutFile get-pip.py"
    if errorlevel 1 (
        echo [ERROR] Could not download get-pip.py. Check your internet connection.
        pause
        exit /b 1
    )
    python get-pip.py --user
    del get-pip.py
)

:: ---- Upgrade pip using --user to avoid access-denied on system paths ----
echo [INFO] Upgrading pip (--user) ...
python -m pip install --upgrade pip --user --quiet 2>nul
:: Non-fatal: some environments forbid even --user upgrades of pip itself

:: ---- Install dependencies -----------------------------------------------
echo.
echo [INFO] Installing Python dependencies (--user) ...
python -m pip install edge-tts fastapi uvicorn pyinstaller --upgrade --user --quiet
if errorlevel 1 (
    echo [WARN] --user install failed, retrying without --user flag ...
    python -m pip install edge-tts fastapi uvicorn pyinstaller --upgrade --quiet
    if errorlevel 1 (
        echo [ERROR] pip install failed. See messages above.
        pause
        exit /b 1
    )
)
echo [OK] Dependencies ready.

:: ---- Locate pyinstaller (may be in user Scripts dir after --user) -------
:: After a --user install the exe lands in %APPDATA%\Python\PythonXY\Scripts
:: which may not be on PATH yet in this session. We find it explicitly.
set PYINST=pyinstaller
where pyinstaller >nul 2>&1
if errorlevel 1 (
    echo [INFO] pyinstaller not in PATH, searching user Scripts folder ...
    for /f "tokens=*" %%p in ('python -c "import sysconfig; print(sysconfig.get_path(\"scripts\",\"nt_user\"))" 2^>nul') do (
        if exist "%%p\pyinstaller.exe" (
            set PYINST=%%p\pyinstaller.exe
            echo [OK] Found pyinstaller at: %%p\pyinstaller.exe
        )
    )
)

:: Final check
"%PYINST%" --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] pyinstaller is still not accessible after install.
    echo         Try closing and reopening this terminal, then run build.bat again.
    pause
    exit /b 1
)

:: ---- Clean previous build artifacts ------------------------------------
echo.
echo [INFO] Cleaning previous build artifacts ...
if exist build   rmdir /s /q build
if exist dist    rmdir /s /q dist
echo [OK] Clean done.

:: ---- Run PyInstaller ----------------------------------------------------
echo.
echo [INFO] Running PyInstaller (this may take 1-3 minutes) ...
"%PYINST%" tts_server.spec --noconfirm
if errorlevel 1 (
    echo [ERROR] PyInstaller failed. See output above for details.
    pause
    exit /b 1
)

:: ---- Verify output ------------------------------------------------------
echo.
if exist "dist\tts_server\tts_server.exe" (
    echo [OK] One-folder build:   dist\tts_server\tts_server.exe
) else (
    echo [WARN] One-folder exe not found.
)

if exist "dist\tts_server_onefile.exe" (
    echo [OK] Single-file build:  dist\tts_server_onefile.exe
) else (
    echo [WARN] Single-file exe not found.
)

:: ---- Summary ------------------------------------------------------------
echo.
echo ============================================================
echo  Build complete!
echo.
echo  Copy the ENTIRE folder into your C# project output dir:
echo    dist\tts_server\  ->  MyApp\bin\Debug\net8.0-windows\tts_server\
echo.
echo  Add to your .csproj:
echo    ^<Content Include="tts_server\**\*"^>
echo      ^<CopyToOutputDirectory^>PreserveNewest^</CopyToOutputDirectory^>
echo    ^</Content^>
echo ============================================================
echo.
pause
