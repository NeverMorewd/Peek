using Microsoft.Extensions.Logging;
using Peek.Ipc.Protocol;
using Peek.Ipc.Transport;
using ReactiveUI.Primitives.Signals;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReactiveUI.Primitives;

namespace Peek.Ipc.Channel;

public sealed class JsonRpcChannel : IJsonRpcChannel
{

    private readonly IPipeTransport _transport;
    private readonly ILogger<JsonRpcChannel> _logger;
    private readonly TimeSpan _defaultTimeout;


    private int _nextId = 0;

    private readonly ConcurrentDictionary<int, PendingCall> _pending = new();
    private readonly Signal<JsonRpcResponse> _responseSubject = new();
    private readonly IDisposable _lineSubscription;

    public JsonRpcChannel(
        IPipeTransport transport,
        ILogger<JsonRpcChannel> logger,
        TimeSpan? defaultTimeout = null)
    {
        _transport      = transport;
        _logger         = logger;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromMilliseconds(20000);

        _lineSubscription = _transport.ReceivedLines
            .Subscribe(OnLineReceived, OnTransportError);
    }

    public IObservable<JsonRpcResponse> Responses =>
        _responseSubject.AsObservable();

    public async Task<JsonRpcResponse> CallAsync(
        string method,
        object? @params      = null,
        TimeSpan? timeout    = null,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);

        var request = new JsonRpcRequest
        {
            Id     = id,
            Method = method,
            Params = @params,
        };

        var effectiveTimeout = timeout ?? _defaultTimeout;
        var pending          = new PendingCall(id, effectiveTimeout, ct);

        if (!_pending.TryAdd(id, pending))
            throw new InvalidOperationException($"Duplicate request ID {id}");

        string json;
        try
        {
            json = JsonSerializer.Serialize(request, IpcJsonContext.Default.Options);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            pending.Dispose();
            throw new InvalidOperationException("Failed to serialize request", ex);
        }

        _logger.LogTrace("→ [{Id}] {Method}", id, method);

        try
        {
            await _transport.SendLineAsync(json, ct).ConfigureAwait(false);
            return await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
            pending.Dispose();
        }
    }

    private void OnLineReceived(string line)
    {
        JsonRpcResponse response;
        try
        {
            response = JsonSerializer.Deserialize<JsonRpcResponse>(
                line, IpcJsonContext.Default.Options)
                ?? throw new JsonException("Deserialized to null");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse response line: {Line}",
                line.Length > 120 ? line[..120] + "…" : line);
            return;
        }

        _responseSubject.OnNext(response);

        // Route to the waiting caller.
        if (response.Id.HasValue && _pending.TryGetValue(response.Id.Value, out var pending))
        {
            _logger.LogTrace("← [{Id}] success={Success}", response.Id, response.IsSuccess);

            if (response.IsSuccess)
                pending.Complete(response);
            else if (response.Error is not null)
                pending.Fail(new RpcException(response.Error));
            else
                pending.Fail(new InvalidOperationException("Response has no result or error"));
        }
        else
        {
            _logger.LogDebug("Received response for unknown/expired ID {Id}", response.Id);
        }
    }

    private void OnTransportError(Exception ex)
    {
        _logger.LogError(ex, "Transport error – failing all pending calls");

        // Fail every pending call so callers don't hang forever.
        foreach (var (_, pending) in _pending)
            pending.Fail(new IOException("Transport faulted", ex));

        _pending.Clear();
    }


    public void Dispose()
    {
        _lineSubscription.Dispose();
        _responseSubject.OnCompleted();
        _responseSubject.Dispose();

        foreach (var (_, pending) in _pending)
        {
            pending.Fail(new ObjectDisposedException(nameof(JsonRpcChannel)));
            pending.Dispose();
        }
        _pending.Clear();
    }
}
internal sealed class PendingCall : IDisposable
{
    private readonly TaskCompletionSource<JsonRpcResponse> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _timeoutCts;
    private readonly CancellationTokenRegistration _ctReg;
    private readonly CancellationTokenRegistration _timeoutReg;

    internal Task<JsonRpcResponse> Task => _tcs.Task;

    public PendingCall(int id, TimeSpan timeout, CancellationToken callerCt)
    {
        _timeoutCts = new CancellationTokenSource(timeout);

        // Timeout fires → TimeoutException
        _timeoutReg = _timeoutCts.Token.Register(() =>
            _tcs.TrySetException(
                new TimeoutException($"RPC call ID {id} timed out after {timeout}")));

        // Caller cancelled → OperationCanceledException
        _ctReg = callerCt.Register(() =>
            _tcs.TrySetCanceled(callerCt));
    }

    public void Complete(JsonRpcResponse response) =>
        _tcs.TrySetResult(response);

    public void Fail(Exception ex) =>
        _tcs.TrySetException(ex);

    public void Dispose()
    {
        _ctReg.Dispose();
        _timeoutReg.Dispose();
        _timeoutCts.Dispose();
    }
}

// ── JSON options ──────────────────────────────────────────────────────────────

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };
    internal static readonly IntPtrConverter IntPtrConverter = new();
}

public class IntPtrConverter : JsonConverter<IntPtr>
{
    public override IntPtr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long number))
            {
                return new IntPtr(number);
            }
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (string.IsNullOrEmpty(text))
            {
                return IntPtr.Zero;
            }
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexResult))
                {
                    return new IntPtr(hexResult);
                }
            }
            else if (long.TryParse(text, out long strResult))
            {
                return new IntPtr(strResult);
            }
        }

        throw new JsonException($"无法将当前的 JSON 标记类型 {reader.TokenType} 转换为 IntPtr。");
    }

    public override void Write(Utf8JsonWriter writer, IntPtr value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToInt64());
    }
}
