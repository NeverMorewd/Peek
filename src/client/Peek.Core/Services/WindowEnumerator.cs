using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Peek.Core.Models;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

[SupportedOSPlatform("windows7.0")]
public sealed class WindowEnumerator
{
    private readonly ILogger<WindowEnumerator> _logger;
    private readonly Dictionary<uint, string>  _processNameCache = [];
    private const int MaxTextLength = 512;

    public WindowEnumerator(ILogger<WindowEnumerator> logger)
        => _logger = logger;

    public IReadOnlyList<WindowNode> EnumerateAll(bool includeChildren = true)
    {
        _processNameCache.Clear();
        var results = new List<WindowNode>(512);

        PInvoke.EnumWindows((hwnd, _) =>
        {
            try
            {
                results.Add(SnapshotWindow(hwnd, parentHwnd: 0, depth: 0));
                if (includeChildren)
                    EnumerateChildren(hwnd, results, depth: 1);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
            }
            return true;
        }, 0);

        return results;
    }
    public IObservable<WindowNode> EnumerateObservable(bool includeChildren = true)
    {
        return Observable.Create<WindowNode>(observer =>
        {
            _processNameCache.Clear();

            try
            {
                PInvoke.EnumWindows((hwnd, _) =>
                {
                    try
                    {
                        var node = SnapshotWindow(hwnd, parentHwnd: 0, depth: 0);
                        observer.OnNext(node);

                        if (includeChildren)
                        {
                            EnumerateChildrenObservable(hwnd, observer, depth: 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
                    }

                    return true; 
                }, 0);

                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }
            return () => { };
        });
    }
    private void EnumerateChildrenObservable(HWND parentHwnd, 
        IObserver<WindowNode> observer,
        int depth)
    {
        PInvoke.EnumChildWindows(parentHwnd, (hwnd, _) =>
        {
            try
            {
                var node = SnapshotWindow(hwnd, parentHwnd, depth);
                observer.OnNext(node);

                EnumerateChildrenObservable(hwnd, observer, depth + 1);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to snapshot child HWND 0x{Hwnd:X}", hwnd);
            }

            return true;
        }, 0);
    }
    public WindowNode? SnapshotSingle(nint hwnd)
    {
        if (!PInvoke.IsWindow(new HWND(hwnd))) return null;
        try
        {
            var parent = PInvoke.GetAncestor((HWND)hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
            return SnapshotWindow(hwnd, parent, depth: 0);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to snapshot HWND 0x{Hwnd:X}", hwnd);
            return null;
        }
    }

    private void EnumerateChildren(nint parentHwnd, List<WindowNode> results, int depth)
    {
        PInvoke.EnumChildWindows(new HWND(parentHwnd), (hwnd, _) =>
        {
            try { results.Add(SnapshotWindow(hwnd, parentHwnd, depth)); }
            catch (Exception ex) { _logger.LogTrace(ex, "Child HWND 0x{Hwnd:X}", hwnd); }
            return true;
        }, 0);
    }

    private WindowNode SnapshotWindow(nint hwnd, nint parentHwnd, int depth)
    {
        Span<char> titleBuf = stackalloc char[512];
        PInvoke.GetWindowText((HWND)hwnd, titleBuf);

        Span<char> classBuf = stackalloc char[256];
        PInvoke.GetClassName((HWND)hwnd, classBuf);

        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint processId);

        var style = (uint)PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var exStyle = (uint)PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        var ownerHwnd = PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_OWNER);

        PInvoke.GetWindowRect((HWND)hwnd, out RECT r);

        return new WindowNode
        {
            Hwnd = hwnd,
            ParentHwnd = parentHwnd,
            OwnerHwnd = ownerHwnd,
            Title = titleBuf.ToString(),
            ClassName = classBuf.ToString(),
            ProcessId = processId,
            ProcessName = GetProcessName(processId),
            Rect = new WindowRect(r.left, r.top, r.Width, r.Height),
            Style = style,
            ExStyle = exStyle,

            IsVisible = PInvoke.IsWindowVisible((HWND)hwnd),
            IsEnabled = (style & (uint)WINDOW_STYLE.WS_DISABLED) == 0,
            IsMinimized = PInvoke.IsIconic((HWND)hwnd),
            IsMaximized = PInvoke.IsZoomed((HWND)hwnd),

            IsTopmost = (exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0,
            IsToolWindow = (exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            IsRoot  = parentHwnd == 0,

            Depth = depth
        };
    }

    private unsafe string GetProcessName(uint pid)
    {
        if (_processNameCache.TryGetValue(pid, out var cached))
            return cached;

        HANDLE handle = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);

        if (handle == HANDLE.Null)
        {
            _processNameCache[pid] = string.Empty;
            return string.Empty;
        }

        // Wrap the raw HANDLE in a SafeFileHandle for PInvoke.CloseHandle
        using var safeHandle = new SafeProcessHandle((HWND)handle.Value, ownsHandle: true);

        // No 'unsafe' block needed since we are using Span<char>
        Span<char> buffer = stackalloc char[1024];
        uint size = (uint)buffer.Length;

        // Argument 3 now correctly receives the Span<char> buffer
        if (PInvoke.QueryFullProcessImageName(safeHandle, 0, buffer, ref size))
        {
            // size is modified by the API to represent the characters written
            var fullPath = buffer[..(int)size].ToString();
            var name = Path.GetFileNameWithoutExtension(fullPath);

            _processNameCache[pid] = name;
            return name;
        }

        return string.Empty;
    }
}
