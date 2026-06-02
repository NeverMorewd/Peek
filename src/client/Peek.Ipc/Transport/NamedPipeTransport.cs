using System.IO.Pipes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Peek.Ipc.Transport;

public sealed class NamedPipeTransport : IPipeTransport
{

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<NamedPipeTransport> _logger;
    private readonly Subject<string> _lineSubject = new();

    private readonly BehaviorSubject<TransportState> _stateSubject =
        new(TransportState.Disconnected);

    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    private readonly object _stateLock = new();

    public NamedPipeTransport(
        string pipeName,
        TimeSpan connectTimeout,
        ILogger<NamedPipeTransport> logger)
    {
        _pipeName       = pipeName;
        _connectTimeout = connectTimeout;
        _logger         = logger;
    }
    public IObservable<string> ReceivedLines =>
        _lineSubject.AsObservable();

    public IObservable<TransportState> State =>
        _stateSubject.AsObservable();

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_stateSubject.Value == TransportState.Connected) return;
            SetState(TransportState.Connecting);
        }

        _logger.LogInformation("Connecting to pipe: {PipeName}", _pipeName);

        var pipe = new NamedPipeClientStream(
            serverName:        ".",
            pipeName:          _pipeName,
            direction:         PipeDirection.InOut,
            options:           PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            SetState(TransportState.Faulted);
            throw new TimeoutException(
                $"Timed out connecting to pipe '{_pipeName}' after {_connectTimeout}");
        }
        catch (Exception ex)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            SetState(TransportState.Faulted);
            _logger.LogError(ex, "Failed to connect to pipe {PipeName}", _pipeName);
            throw;
        }

        lock (_stateLock)
        {
            _pipe   = pipe;
            _writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = false,
                NewLine   = "\n"    // LF only – matches Rust writeln!
            };

            _readCts  = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));

            SetState(TransportState.Connected);
        }

        _logger.LogInformation("Connected to pipe: {PipeName}", _pipeName);
    }

    public async Task SendLineAsync(string line, CancellationToken ct = default)
    {
        if (_stateSubject.Value != TransportState.Connected)
            throw new InvalidOperationException("Transport is not connected.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_writer is null) throw new InvalidOperationException("Writer is null.");
            await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Write failed – marking transport as faulted");
            SetState(TransportState.Faulted);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? cts;
        Task? readTask;

        lock (_stateLock)
        {
            cts      = _readCts;
            readTask = _readTask;
            _readCts  = null;
            _readTask = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        if (readTask is not null)
        {
            try { await readTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        lock (_stateLock)
        {
            _writer?.Dispose();
            _writer = null;
            _pipe?.Dispose();
            _pipe = null;
            SetState(TransportState.Disconnected);
        }

        _logger.LogInformation("Disconnected from pipe: {PipeName}", _pipeName);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Read loop started");

        try
        {
            using var reader = new StreamReader(_pipe!, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (line is null)
                {
                    _logger.LogInformation("Pipe closed by worker (EOF)");
                    SetState(TransportState.Faulted);
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogTrace("← {Line}", line.Length > 200
                        ? line[..200] + "…" : line);
                    _lineSubject.OnNext(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read loop faulted");
            SetState(TransportState.Faulted);
        }

        _logger.LogDebug("Read loop stopped");
    }

    private void SetState(TransportState state)
    {
        if (_stateSubject.Value != state)
        {
            _logger.LogDebug("Transport state: {State}", state);
            _stateSubject.OnNext(state);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _lineSubject.OnCompleted();
        _lineSubject.Dispose();
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
        _writeLock.Dispose();
    }
}
