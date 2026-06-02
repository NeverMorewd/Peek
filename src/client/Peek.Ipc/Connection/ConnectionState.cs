
namespace Peek.Ipc.Connection;

public enum ConnectionState
{
    /// Not yet started or cleanly shut down.
    Idle,

    /// Starting the worker process.
    StartingWorker,

    /// Worker is running, pipe connection in progress.
    Connecting,

    /// Pipe connected, RPC channel ready.
    Ready,

    /// Connection lost; waiting before next reconnect attempt.
    Reconnecting,

    /// Permanently stopped (Dispose called).
    Stopped,
}

public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState Previous { get; init; }
    public ConnectionState Current  { get; init; }
    public Exception?      Reason   { get; init; }
}
