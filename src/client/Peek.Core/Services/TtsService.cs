using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Peek.Core.Services;

public sealed class TtsService : IDisposable
{
    private const string ServerExeName = "tts_server.exe";
    private const string Host = "127.0.0.1";
    private const int Port = 5050;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private static string BaseUrl => $"http://{Host}:{Port}";
    private Process? _serverProcess;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _started;
    private bool _disposed;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _speakLock = new(1, 1);

    public TtsService(ILogger<TtsService> logger) 
    {
        _logger = logger;
    }

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_started) return;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_started) return;
            await StartServerAsync(ct);
            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<byte[]> GetVoiceAsync(
        string text,
        string voice = "zh-CN-XiaoxiaoNeural",
        string rate = "+0%",
        string pitch = "+0Hz",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        await EnsureStartedAsync(ct);

        ct.ThrowIfCancellationRequested();

        string url = $"{BaseUrl}/speak"
            + $"?text={Uri.EscapeDataString(text)}"
            + $"&voice={Uri.EscapeDataString(voice)}"
            + $"&rate={Uri.EscapeDataString(rate)}"
            + $"&pitch={Uri.EscapeDataString(pitch)}";

        byte[] audioBytes = await _http.GetByteArrayAsync(url, ct);

        ct.ThrowIfCancellationRequested();
        return audioBytes;
    }

    public async Task<JsonElement[]> GetVoicesAsync(CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);

        string json = await _http.GetStringAsync($"{BaseUrl}/voices", ct);
        return JsonSerializer.Deserialize<JsonElement[]>(json) ?? [];
    }

    private async Task StartServerAsync(CancellationToken ct)
    {
        string exePath = Path.Combine(AppContext.BaseDirectory, ServerExeName);

        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"TTS server executable not found. Expected: {exePath}", exePath);

        await KillExistingTtsProcesses(exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--host {Host} --port {Port}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["ParentId"] = Environment.ProcessId.ToString();
        _serverProcess = new Process { StartInfo = psi , EnableRaisingEvents  = true };
        _serverProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[TTS-Server] {e.Data}");
        };
        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Debug.WriteLine($"[TTS-Server ERR] {e.Data}");
        };
        _serverProcess.Exited += (_, _) =>
        {
            _logger.LogWarning("TTS server exited accidentally!");
            _started = false;
        };
        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        bool ready = await WaitForReadyAsync(ct);
        if (!ready)
            throw new TimeoutException("TTS server did not become ready within the timeout.");
    }

    private async Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(StartupTimeout);

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(300, timeoutCts.Token);
                var resp = await _http.GetAsync($"{BaseUrl}/health", timeoutCts.Token);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch (OperationCanceledException) { break; }
            catch { /* not ready yet – keep polling */ }
        }
        return false;
    }
    private static async Task KillExistingTtsProcesses(string expectedExePath)
    {
        var processName = Path.GetFileNameWithoutExtension(expectedExePath);
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited) continue;

                process.Kill(entireProcessTree: false);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(cts.Token);
            }
            catch (Win32Exception)
            {
  
            }
            catch (InvalidOperationException)
            {
 
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_serverProcess is { HasExited: false })
            {
                _serverProcess.Kill(entireProcessTree: false);
                _serverProcess.WaitForExitAsync();
            }
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"Kill process error (ignored): {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unexpected error killing TTS process: {ex}");
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
        }

        _http.Dispose();
        _startLock.Dispose();
        _speakLock.Dispose();
    }
}