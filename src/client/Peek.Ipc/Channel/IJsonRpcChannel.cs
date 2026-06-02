
using Peek.Ipc.Protocol;

namespace Peek.Ipc.Channel;

public interface IJsonRpcChannel : IDisposable
{
    Task<JsonRpcResponse> CallAsync(
        string method,
        object? @params     = null,
        TimeSpan? timeout   = null,
        CancellationToken ct = default);

    IObservable<JsonRpcResponse> Responses { get; }
}
