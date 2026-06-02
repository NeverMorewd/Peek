using Peek.Ipc.Protocol;

namespace Peek.Ipc.Client;

/// <summary>
/// Typed async API for querying the Peek UIA worker.
/// All methods throw <see cref="Channel.RpcException"/> on worker-side errors.
/// </summary>
public interface IWorkerClient
{
    /// <summary>
    /// Find the UI element at the given screen coordinates.
    /// Returns null when no element exists at that point.
    /// </summary>
    Task<ElementInfo?> GetElementFromPointAsync(
        int x, int y,
        CancellationToken ct = default);

    /// <summary>Find the root UIA element for a window handle.</summary>
    Task<ElementInfo?> GetElementFromHandleAsync(
        nint hwnd,
        CancellationToken ct = default);

    /// <summary>
    /// Enumerate UIA children of the element identified by <paramref name="hwnd"/>.
    /// <paramref name="depth"/> controls recursion levels (0 = direct children).
    /// </summary>
    Task<IReadOnlyList<ElementInfo>> GetChildrenAsync(
        nint hwnd,
        int depth            = 0,
        CancellationToken ct = default);

    /// <summary>Force the worker to clear its element cache.</summary>
    Task ClearCacheAsync(CancellationToken ct = default);

    /// <summary>Retrieve health and performance stats from the worker.</summary>
    Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Round-trip latency check. Returns true on success.</summary>
    Task<bool> PingAsync(CancellationToken ct = default);
}
