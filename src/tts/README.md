# EdgeTTS Server – Integration Guide

A lightweight local HTTP microservice that wraps **edge-tts** (Microsoft Edge TTS)
for use by a C# / WPF host application.  No API key, no Edge browser required.

---

## Contents

```
tts_server.py       – FastAPI microservice source
tts_server.spec     – PyInstaller build spec
build.bat           – One-click Windows build script
build.py            – Cross-platform Python build script
TtsService.cs       – Drop-in C# service class for WPF
README.md           – This file
```

---

## 1. Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| Python | 3.9 – 3.12 | 3.11 recommended |
| pip | any | bundled with Python |
| Internet access | – | edge-tts calls Microsoft's TTS service |

---

## 2. Build the executable

### Windows (recommended)

Double-click **`build.bat`** or run it from a terminal:

```bat
cd path\to\tts_project
build.bat
```

### Cross-platform (CI / macOS / Linux)

```bash
python build.py
# or: python build.py --no-onefile   (skip single-file variant, faster)
```

### Output

```
dist/
  tts_server/               ← one-folder build  ← USE THIS in C#
    tts_server.exe
    ...DLLs / .pyd files...
  tts_server_onefile.exe    ← single .exe (slower cold start, ~5 s extraction)
```

> **Recommendation:** use the **one-folder** build for your C# project.
> Cold-start is ~1–2 s vs ~5–8 s for the single-file variant.

---

## 3. Integrate into your C# / WPF project

### 3.1 Copy the server folder

Copy the entire `dist/tts_server/` folder into your C# project, e.g.:

```
MyApp/
  tts_server/        ← paste here
  MyApp.csproj
  ...
```

### 3.2 Add to .csproj

```xml
<ItemGroup>
  <!-- Copy the entire tts_server folder to the build output directory -->
  <Content Include="tts_server\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### 3.3 Add TtsService.cs

Copy `TtsService.cs` into your project and adjust the namespace if needed.

Make sure `PresentationCore` is referenced (it's included by default in WPF projects).

### 3.4 Wire up in App.xaml.cs

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Pre-warm the TTS server in the background
    _ = TtsService.Instance.EnsureStartedAsync();
}

protected override void OnExit(ExitEventArgs e)
{
    TtsService.Instance.Dispose();
    base.OnExit(e);
}
```

### 3.5 Speak text

```csharp
// Read the selected UI element's text
await TtsService.Instance.SpeakAsync(selectedElement.Name);

// Custom voice / rate
await TtsService.Instance.SpeakAsync(
    text:  "Hello, world!",
    voice: "en-US-AriaNeural",
    rate:  "+10%"
);
```

---

## 4. HTTP API reference

All endpoints bind to `http://127.0.0.1:5050` by default.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Liveness probe – returns `{"status":"ok"}` |
| `GET` | `/voices` | JSON array of all available voices |
| `GET` | `/speak` | Stream MP3 audio for `?text=...` |
| `GET` | `/speak/save` | Save MP3 to temp file, return `{"path":"..."}` |

### `/speak` query parameters

| Parameter | Default | Example |
|-----------|---------|---------|
| `text` | *(required)* | `Hello world` |
| `voice` | `zh-CN-XiaoxiaoNeural` | `en-US-AriaNeural` |
| `rate` | `+0%` | `+20%` or `-10%` |
| `volume` | `+0%` | `+50%` |
| `pitch` | `+0Hz` | `+50Hz` |

---

## 5. Useful voices

### Chinese

| Short Name | Gender | Style |
|-----------|--------|-------|
| `zh-CN-XiaoxiaoNeural` | Female | Warm, natural (recommended) |
| `zh-CN-YunxiNeural` | Male | Standard |
| `zh-CN-XiaoyiNeural` | Female | Lively |
| `zh-TW-HsiaoChenNeural` | Female | Traditional Chinese |

### English

| Short Name | Gender | Style |
|-----------|--------|-------|
| `en-US-AriaNeural` | Female | Natural |
| `en-US-GuyNeural` | Male | Standard |
| `en-GB-SoniaNeural` | Female | British |

Fetch all voices at runtime:

```csharp
var voices = await TtsService.Instance.GetVoicesAsync();
```

Or via CLI:

```bat
edge-tts --list-voices
```

---

## 6. Troubleshooting

| Symptom | Fix |
|---------|-----|
| `FileNotFoundException: tts_server.exe` | Verify the `tts_server/` folder is in the same directory as your `.exe`. Check `.csproj` `CopyToOutputDirectory`. |
| `TimeoutException` on startup | Antivirus may be blocking the new process. Whitelist `tts_server.exe`. |
| No audio / silent playback | Check that `PresentationCore` is referenced. Ensure audio output device is available. |
| HTTP 502 from `/speak` | The machine has no internet access. edge-tts requires connectivity to `api.msedgeservices.com`. |
| Port conflict | Change `Port = 5050` in `TtsService.cs` and `--port 5050` in the startup args to another free port. |

---

## 7. License

edge-tts is MIT licensed.  See https://github.com/rany2/edge-tts for details.
This integration code is provided as-is for use in your project.
