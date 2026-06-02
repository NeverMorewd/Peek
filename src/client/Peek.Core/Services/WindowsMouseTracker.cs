using Peek.Core.Abstractions;
using Peek.Ipc.Connection;
using System.Drawing;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

[SupportedOSPlatform("windows6.0")]
public sealed class WindowsMouseTracker : IMouseTracker, IDisposable
{
    private const int SendKeyDelayMs = 100;
    private const int MinTextLength = 1;

    private const uint WM_LBUTTONUP = 0x0202;


    private readonly BehaviorSubject<bool> _enabled = new(true);
    private readonly Subject<string> _selectedTextSubject = new();
    private readonly WorkerConnection _workerConnection;
    private readonly IClipboardService _clipboard;

    private readonly WindowsHookService _hookService;
    private int _capturing;
    private int _disposed; 


    public IObservable<Point> MousePositionStream { get; }
    public IObservable<string> SelectedTextStream { get; }

    public WindowsMouseTracker(
        WorkerConnection workerConnection,
        IClipboardService clipboard,
        WindowsHookService hookService,
        int intervalMs = 20)
    {
        _workerConnection = workerConnection;
        _clipboard = clipboard;
        _hookService = hookService;

        _hookService.RegisterLowLevelHook(WINDOWS_HOOK_ID.WH_MOUSE_LL);
        _hookService.HookFired += OnHookFired;

        MousePositionStream =
            Observable.Interval(TimeSpan.FromMilliseconds(intervalMs))
                .Select(_ =>
                {
                    PInvoke.GetCursorPos(out var p);
                    return p;
                })
                .Where(pos => !IsOwnWindow(pos.X, pos.Y))
                .CombineLatest(_enabled, (pos, enabled) => (pos, enabled))
                .Where(x => x.enabled)
                .Select(x => x.pos)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();

        SelectedTextStream = _selectedTextSubject
            .CombineLatest(_enabled, (text, enabled) => (text, enabled))
            .Where(x => x.enabled && !string.IsNullOrWhiteSpace(x.text))
            .Select(x => x.text)
            .DistinctUntilChanged()
            .Publish()
            .RefCount();
    }


    public void Pause() => _enabled.OnNext(false);
    public void Resume() => _enabled.OnNext(true);

    private void OnHookFired(object? sender, WindowsHookEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        if (e.WParam == WM_LBUTTONUP && _enabled.Value)
            _ = CaptureSelectedTextAsync();
    }

    private async Task CaptureSelectedTextAsync()
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0)
            return;

        try
        {
            string? backup = await _clipboard.GetTextAsync();
            uint beforeSeq = GetClipboardSequenceNumberSafe();

            SendCtrlC();

            await Task.Delay(SendKeyDelayMs).ConfigureAwait(false);

            string? captured = await _clipboard.GetTextAsync();
            uint afterSeq = GetClipboardSequenceNumberSafe();

            await _clipboard.SetTextAsync(backup);

            if (!string.IsNullOrWhiteSpace(captured)
                && captured.Length >= MinTextLength
                && captured != backup
                && afterSeq != beforeSeq)
            {
                _selectedTextSubject.OnNext(captured.Trim());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TextSelector] capture error: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _capturing, 0);
        }
    }


    private static uint GetClipboardSequenceNumberSafe()
    {
        try { return PInvoke.GetClipboardSequenceNumber(); }
        catch { return 0; }
    }

    private static unsafe void SendCtrlC()
    {
        Span<INPUT> inputs =
        [
            MakeKeyInput(VIRTUAL_KEY.VK_CONTROL, keyUp: false),
            MakeKeyInput(VIRTUAL_KEY.VK_C,       keyUp: false),
            MakeKeyInput(VIRTUAL_KEY.VK_C,       keyUp: true),
            MakeKeyInput(VIRTUAL_KEY.VK_CONTROL, keyUp: true),
        ];
        fixed (INPUT* pInputs = inputs)
            _ = PInvoke.SendInput((uint)inputs.Length, pInputs, sizeof(INPUT));
    }

    private static INPUT MakeKeyInput(VIRTUAL_KEY vk, bool keyUp) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = UIntPtr.Zero,
            }
        }
    };

    private static bool IsOwnWindow(int x, int y)
    {
        var hwnd = PInvoke.WindowFromPoint(new Point(x, y));
        PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }


    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.HookFired -= OnHookFired;
        SpinWait.SpinUntil(() => Volatile.Read(ref _capturing) == 0, 2000);

        _selectedTextSubject.OnCompleted();
        _selectedTextSubject.Dispose();

        _enabled.OnCompleted();
        _enabled.Dispose();
    }
}