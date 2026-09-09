using Peek.Core.Abstractions;
using Peek.Ipc.Connection;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Drawing;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

[SupportedOSPlatform("windows6.0")]
public sealed class WindowsMouseTracker : IMouseTracker, IDisposable
{
    private const uint WM_LBUTTONUP = 0x0202;

    private readonly BehaviorSignal<bool> _enabled = new(true);
    private readonly Signal<RxVoid> _selectedTextSubject = new();
    private readonly WorkerConnection _workerConnection;
    private readonly WindowsHookService _hookService;
    private int _disposed;

    public IObservable<Point> MousePositionStream { get; }
    public IObservable<RxVoid> SelectedStream { get; }

    public WindowsMouseTracker(
        WorkerConnection workerConnection,
        WindowsHookService hookService,
        int intervalMs = 20)
    {
        _workerConnection = workerConnection;
        _hookService = hookService;

        _hookService.RegisterLowLevelHook(WINDOWS_HOOK_ID.WH_MOUSE_LL);
        _hookService.HookFired += OnHookFired;

        MousePositionStream =
            Signal.Interval(TimeSpan.FromMilliseconds(intervalMs))
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

        SelectedStream = _selectedTextSubject
            .CombineLatest(_enabled, (unit, enabled) => (unit, enabled))
            .Where(x => x.enabled)
            .Select(x => x.unit)
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
            _selectedTextSubject.OnNext(RxVoid.Default);
    }

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

        _selectedTextSubject.OnCompleted();
        _selectedTextSubject.Dispose();

        _enabled.OnCompleted();
        _enabled.Dispose();
    }
}