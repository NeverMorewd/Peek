using System.Text.Json;
using Peek.Ipc.Channel;
using Peek.Ipc.Protocol;
using Microsoft.Extensions.Logging;

namespace Peek.Ipc.Client;

public sealed class WorkerClient : IWorkerClient
{
    private readonly IJsonRpcChannel _channel;
    private readonly ILogger<WorkerClient> _logger;

    public WorkerClient(IJsonRpcChannel channel, ILogger<WorkerClient> logger)
    {
        _channel = channel;
        _logger  = logger;
    }

    public async Task<ElementInfo?> GetElementFromPointAsync(
        int x, int y, CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync(
                "get_element_from_point",
                new GetElementFromPointParams { X = x, Y = y },
                ct: ct).ConfigureAwait(false);

            return Deserialize<ElementInfo>(response);
        }
        catch (RpcException ex) when (ex.IsElementNotFound)
        {
            // Normal – no element under cursor.
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetElementFromPoint({X},{Y}) failed", x, y);
            throw;
        }
    }

    public async Task<ElementInfo?> GetElementFromHandleAsync(
        nint hwnd, CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync(
                "get_element_from_handle",
                new GetElementFromHandleParams { Hwnd = hwnd },
                ct: ct).ConfigureAwait(false);

            return Deserialize<ElementInfo>(response);
        }
        catch (RpcException ex) when (ex.IsElementNotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ElementInfo>> GetChildrenAsync(
        nint hwnd, int depth = 0, CancellationToken ct = default)
    {
        var response = await _channel.CallAsync(
            "get_children",
            new GetChildrenParams { Hwnd = hwnd, Depth = depth },
            ct: ct).ConfigureAwait(false);

        return Deserialize<List<ElementInfo>>(response)
            ?? [];
    }

    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        await _channel.CallAsync("clear_cache", ct: ct).ConfigureAwait(false);
    }

    public async Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _channel.CallAsync("get_status", ct: ct)
                .ConfigureAwait(false);

            var status = Deserialize<WorkerStatus>(response);
            if (status is null)
            {
                _logger.LogError($"{response}");
                throw new InvalidOperationException("Invalid response from worker!");
            }
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync");
            throw;
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await _channel.CallAsync("ping", ct: ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static T? Deserialize<T>(JsonRpcResponse response)
    {
        if (!response.Result.HasValue)
            throw new InvalidOperationException("Response has no result");

        return response.Result.Value.Deserialize<T>(JsonOptions.Default);
    }
}
