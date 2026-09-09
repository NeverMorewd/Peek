
using Microsoft.Extensions.Logging;
using Peek.Ipc.Channel;
using Peek.Ipc.Client;
using Peek.Ipc.Transport;
using System.ComponentModel;
using System.Diagnostics;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace Peek.Ipc.Connection;

public sealed class WorkerConnection(WorkerConnectionOptions options,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly WorkerConnectionOptions _options = options;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<WorkerConnection> _logger = loggerFactory.CreateLogger<WorkerConnection>();

    private readonly BehaviorSignal<ConnectionState> _stateSubject =
        new(ConnectionState.Idle);

    public IObservable<ConnectionState> State => _stateSubject.AsObservable();

    public ConnectionState CurrentState => _stateSubject.Value;

    private NamedPipeTransport? _transport;
    private JsonRpcChannel?     _channel;
    private WorkerClient?       _client;
    private IDisposable?        _transportStateSub;

    public IWorkerClient Client =>
        _client ?? throw new InvalidOperationException(
            $"Worker is not ready (state={CurrentState})");


    private Process? _workerProcess;


    private CancellationTokenSource _lifetimeCts = new();
    private Task _watchdogLoop  = Task.CompletedTask;
    private readonly SemaphoreSlim _connectLock = new(1, 1);


    public async Task StartAsync(CancellationToken ct = default)
    {
        _lifetimeCts = new CancellationTokenSource();

        SetState(ConnectionState.StartingWorker);
        await LaunchWorkerProcessAsync(ct).ConfigureAwait(false);

        await ConnectWithRetryAsync(_lifetimeCts.Token).ConfigureAwait(false);

        _watchdogLoop = Task.Run(
            () => WatchdogLoopAsync(_lifetimeCts.Token),
            _lifetimeCts.Token);
    }

    public async Task StopAsync()
    {
        SetState(ConnectionState.Stopped);

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);

        try 
        { 
            await _watchdogLoop.ConfigureAwait(false); 
        }
        catch (OperationCanceledException) 
        {  
        }

        await TearDownStackAsync().ConfigureAwait(false);
        KillWorkerProcess();
    }

    private async Task LaunchWorkerProcessAsync(CancellationToken ct)
    {
        if (_options.ManageWorkerProcess == false)
        {
            _logger.LogInformation("ManageWorkerProcess=false – assuming worker is already running");
        }
        _logger.LogInformation("Launching worker: {Path}", _options.WorkerExecutablePath);

        var psi = new ProcessStartInfo
        {
            FileName               = _options.WorkerExecutablePath,
            UseShellExecute        = false,
            CreateNoWindow         = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = false,
        };

        _workerProcess = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start worker process: {_options.WorkerExecutablePath}");

        _workerProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger.LogDebug("[uia-worker] {Line}", e.Data);
        };
        _workerProcess.BeginErrorReadLine();

        _workerProcess.EnableRaisingEvents = true;
        _workerProcess.Exited += OnWorkerExited;

        _logger.LogWarning(
            "Worker started (PID={Pid})", _workerProcess.Id);

        await Task.Delay(_options.WorkerStartupDelay, ct);
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        if (_stateSubject.Value == ConnectionState.Stopped) return;

        _logger.LogWarning(
            "Worker process exited unexpectedly (code={Code})",
            _workerProcess?.ExitCode);

        _ = TriggerReconnectAsync();
    }

    private async Task TriggerReconnectAsync()
    {
        if (_stateSubject.Value is ConnectionState.Reconnecting
                                or ConnectionState.Stopped) return;

        SetState(ConnectionState.Reconnecting);
        await TearDownStackAsync().ConfigureAwait(false);

        if (_options.ManageWorkerProcess)
        {
            KillWorkerProcess();
            try
            {
                await LaunchWorkerProcessAsync(_lifetimeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart worker process");
            }
        }

        await ConnectWithRetryAsync(_lifetimeCts.Token).ConfigureAwait(false);
    }

    private void KillWorkerProcess()
    {
        if (_workerProcess is null) return;
        try
        {
            if (!_workerProcess.HasExited)
            {
                _logger.LogInformation("Killing worker (PID={Pid})", _workerProcess.Id);

                try
                {
                    _workerProcess.Kill(entireProcessTree: false);
                }
                catch (Win32Exception ex) when (_workerProcess.HasExited)
                {
                    _logger.LogDebug(
                        "Worker (PID={Pid}) already exited before Kill completed (Win32={Code})",
                        _workerProcess.Id, ex.NativeErrorCode);
                }
                catch (InvalidOperationException)
                {
                    // Process object has no OS handle — already fully cleaned up.
                    _logger.LogDebug("Worker process handle was already released");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill worker process");
        }
        finally
        {
            _workerProcess.Exited -= OnWorkerExited;
            _workerProcess.Dispose();
            _workerProcess = null;
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 1; !ct.IsCancellationRequested; attempt++)
            {
                SetState(ConnectionState.Connecting);
                _logger.LogInformation("Connect attempt {Attempt}…", attempt);

                try
                {
                    await BuildAndConnectStackAsync(ct).ConfigureAwait(false);
                    SetState(ConnectionState.Ready);
                    _logger.LogInformation("IPC connection established");
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Connect attempt {Attempt} failed – retrying in {Delay}ms",
                        attempt, _options.ReconnectDelay.TotalMilliseconds);

                    await TearDownStackAsync().ConfigureAwait(false);

                    await Task.Delay(
                        _options.ReconnectDelay, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task BuildAndConnectStackAsync(CancellationToken ct)
    {
        var transport = new NamedPipeTransport(
            pipeName:       _options.PipeName,
            connectTimeout: _options.ConnectTimeout,
            logger: _loggerFactory.CreateLogger<NamedPipeTransport>());

        await transport.ConnectAsync(ct).ConfigureAwait(false);

        var channel = new JsonRpcChannel(
            transport,
            _loggerFactory.CreateLogger<JsonRpcChannel>(),
            _options.RpcTimeout);

        var client = new WorkerClient(
            channel,
            _loggerFactory.CreateLogger<WorkerClient>());

        // When the transport faults, trigger a reconnect automatically.
        var sub = transport.State
            .Where(s => s == TransportState.Faulted)
            .Subscribe(async _ =>
            {
                _logger.LogWarning("Transport faulted – scheduling reconnect");
               await TriggerReconnectAsync();
            });

        // Atomically replace the active stack.
        await TearDownStackAsync().ConfigureAwait(false);

        _transport         = transport;
        _channel           = channel;
        _client            = client;
        _transportStateSub = sub;
    }

    private async Task TearDownStackAsync()
    {
        _transportStateSub?.Dispose();
        _transportStateSub = null;

        _channel?.Dispose();
        _channel = null;
        _client  = null;

        if (_transport is not null)
        {
            await _transport.DisconnectAsync().ConfigureAwait(false);
            _transport.Dispose();
            _transport = null;
        }
    }


    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Watchdog started");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.WatchdogInterval, ct).ConfigureAwait(false);

            if (_stateSubject.Value != ConnectionState.Ready) continue;

            try
            {
                using var timeoutCts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.WatchdogTimeout);

                var alive = await Client.PingAsync(timeoutCts.Token)
                    .ConfigureAwait(false);

                if (!alive)
                {
                    _logger.LogWarning("Watchdog ping returned false");
                    await TriggerReconnectAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchdog ping failed – triggering reconnect");
                await TriggerReconnectAsync().ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Watchdog stopped");
    }

    private void SetState(ConnectionState next)
    {
        var prev = _stateSubject.Value;
        if (prev == next) return;
        _logger.LogInformation("Connection: {Prev} → {Next}", prev, next);
        _stateSubject.OnNext(next);
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_stateSubject.Value != ConnectionState.Stopped)
            await StopAsync().ConfigureAwait(false);

        _lifetimeCts.Dispose();
        _connectLock.Dispose();
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
    }
}
