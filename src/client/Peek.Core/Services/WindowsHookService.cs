using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

public sealed class WindowsHookEventArgs : EventArgs
{
    public int NCode { get; init; }
    public nuint WParam { get; init; }
    public nint LParam { get; init; }
}

public sealed class WinEventArgs : EventArgs
{
    public uint EventType { get; init; }
    public IntPtr WindowHandle { get; init; }
    public int ObjectId { get; init; }
    public int ChildId { get; init; }
    public uint EventThread { get; init; }
    public uint EventTimeMs { get; init; }
}

[SupportedOSPlatform("windows6.0")]
public sealed class WindowsHookService : IDisposable
{

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly record struct LowLevelHookEntry(
        HOOKPROC Delegate,
        UnhookWindowsHookExSafeHandle Handle);

    private readonly record struct WinEventEntry(
        WINEVENTPROC Delegate,
        HWINEVENTHOOK Handle);

    private readonly List<LowLevelHookEntry> _llHooks = [];
    private readonly List<WinEventEntry> _winEvents = [];

    private int _disposed; 


    public event EventHandler<WindowsHookEventArgs>? HookFired;

    public event EventHandler<WinEventArgs>? WinEventFired;

    public void RegisterLowLevelHook(WINDOWS_HOOK_ID hookId)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        HOOKPROC proc = (nCode, wParam, lParam) =>
            LowLevelCallback(hookId, nCode, wParam, lParam);

        using var module = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        var hMod = PInvoke.GetModuleHandle(module.ModuleName);
        var handle = PInvoke.SetWindowsHookEx(hookId, proc, hMod, 0);

        if (handle.IsInvalid)
            throw new InvalidOperationException(
                $"SetWindowsHookEx({hookId}) failed: 0x{Marshal.GetLastWin32Error():X8}");

        _llHooks.Add(new LowLevelHookEntry(proc, handle));
    }

    public void RegisterWinEvent(uint eventMin, uint eventMax)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        WINEVENTPROC proc = OnWinEventCallback;

        var handle = PInvoke.SetWinEventHook(
            eventMin, 
            eventMax,
            HMODULE.Null,
            proc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWinEventHook(0x{eventMin:X4}–0x{eventMax:X4}) failed: " +
                $"0x{Marshal.GetLastWin32Error():X8}");

        _winEvents.Add(new WinEventEntry(proc, handle));
    }

    private LRESULT LowLevelCallback(
        WINDOWS_HOOK_ID hookId, int nCode, WPARAM wParam, LPARAM lParam)
    {
        var entry = _llHooks.FirstOrDefault(e =>
            e.Handle is { IsInvalid: false });

        var next = entry.Handle is { IsInvalid: false }
            ? PInvoke.CallNextHookEx(entry.Handle, nCode, wParam, lParam)
            : default;

        if (nCode >= 0 && Volatile.Read(ref _disposed) == 0)
        {
            HookFired?.Invoke(this, new WindowsHookEventArgs
            {
                NCode = nCode,
                WParam = wParam.Value,
                LParam = lParam.Value,
            });
        }

        return next;
    }

    private void OnWinEventCallback(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        WinEventFired?.Invoke(this, new WinEventArgs
        {
            EventType = @event,
            WindowHandle = hwnd,
            ObjectId = idObject,
            ChildId = idChild,
            EventThread = idEventThread,
            EventTimeMs = dwmsEventTime,
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        foreach (var entry in _llHooks)
        {
            if (!entry.Handle.IsInvalid)
                entry.Handle.Dispose(); 
        }
        _llHooks.Clear();

        foreach (var entry in _winEvents)
        {
            if (entry.Handle != IntPtr.Zero)
                PInvoke.UnhookWinEvent(entry.Handle);
        }
        _winEvents.Clear();
    }
}