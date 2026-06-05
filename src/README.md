# Peek

Peek is a desktop UI inspection and text-to-speech project with a .NET/Avalonia
client, a Rust UI Automation worker, and a Python Edge TTS service.

## Credits

### .NET client


| Dependency | Version | Notes |
| --- | --- | --- |
| AsyncNavigation | 2.0.2 | Navigation framework for Wpf/Avalonia applications. |
| Avalonia | 12.0.3 | Cross-platform UI framework. |
| LibVLCSharp | 3.9.7.1 | .NET bindings for libVLC. |
| Microsoft.Windows.CsWin32 | 0.3.275 | Source generator for Windows API bindings. |
| Pipboy.Avalonia | 1.1.1-preview.13 | Avalonia UI library. |
| ProDataGrid | 12.0.0 | Data grid control for Avalonia. |
| ReactiveUI.Avalonia | 12.0.1 | Avalonia integration for ReactiveUI. |
| ReactiveUI.SourceGenerators | 2.6.30 | Source generators for ReactiveUI. |
| SkiaSharp | 3.119.3-preview.1.1 | 2D graphics API for .NET. |

### Rust


| Dependency | Version | Notes |
| --- | --- | --- |
| uiautomation | 0.22 | Windows UI Automation wrapper. |
| windows | 0.58 | Rust bindings for Windows APIs. |
| interprocess | 2 | Inter-process communication primitives. |
| serde | 1 | Serialization framework. |
| serde_json | 1 | JSON support for Serde. |
| tracing | 0.1 | Structured diagnostics and logging. |
| tracing-subscriber | 0.3 | Subscriber implementations for tracing. |
| thiserror | 1 | Error type derivation. |
| anyhow | 1 | Flexible error handling. |
| parking_lot | 0.12 | Synchronization primitives. |

### Python 


| Dependency | Notes |
| --- | --- |
| edge-tts | Microsoft Edge online text-to-speech client. |
| FastAPI | HTTP API framework for the local TTS service. |
| Uvicorn | ASGI server used to host the FastAPI app. |
| PyInstaller | Build tool used to package the Python service as a Windows executable. |

