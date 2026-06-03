// Connection/WorkerConnectionOptions.cs
// All tunable parameters for WorkerConnection in one place.
// Declared as a record so callers can use `with` expressions to derive variants.

namespace Peek.Ipc.Connection;

public sealed record WorkerConnectionOptions
{
    /// <summary>
    /// Named pipe to connect to.
    /// Must match PIPE_NAME in the Rust worker (server.rs).
    /// </summary>
    public string PipeName { get; init; } = "ui-inspector-worker";

    /// <summary>Path to the Rust worker executable.</summary>
    public string WorkerExecutablePath { get; init; } = "ui-inspector-worker.exe";

    /// <summary>
    /// When true, WorkerConnection launches and manages the worker process.
    /// Set to false if the worker is already running externally (e.g. during dev).
    /// </summary>
    public bool ManageWorkerProcess { get; init; } = true;

    /// <summary>How long to wait after launching the worker before connecting.</summary>
    public TimeSpan WorkerStartupDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Timeout for the initial pipe connect call.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Default per-RPC call timeout.</summary>
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Delay between reconnect attempts.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the watchdog pings the worker.</summary>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Watchdog ping timeout – triggers reconnect if exceeded.</summary>
    public TimeSpan WatchdogTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
}
