using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Ipc.Connection;
using Peek.Ipc.Extensions;
using Peek.Ipc.Protocol;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Splat;
using System.Data;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using ConnectionState = Peek.Ipc.Connection.ConnectionState;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Peek.Core.Services;

public partial class ElementTracker : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly SerialDisposable _trackingDisposable = new();
    private readonly WorkerConnection _workerConnection;
    private readonly IMouseTracker _mouseTracker;
    private readonly ILogger _logger;
    private readonly IHighlightService _highlightService;
    private readonly TtsService _tsService;
    private readonly AudioPlayer _audioPlayer;
    private CancellationTokenSource _ttsCts = new();
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    [Reactive]
    private ElementInfo? _currentElement;
    [Reactive]
    private ConnectionState _workerState;
    [Reactive]
    private string _statusText = "";
    [Reactive]
    private bool _isTracking;
    public ElementTracker(IServiceProvider serviceProvider, ILogger<ElementTracker> logger)
    {
        _logger = logger;

        _sw.Restart();
        _mouseTracker = serviceProvider.GetRequiredService<IMouseTracker>();
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _highlightService = serviceProvider.GetRequiredService<IHighlightService>();
        _tsService = serviceProvider.GetRequiredService<TtsService>();
        _audioPlayer = serviceProvider.GetRequiredService<AudioPlayer>();
        _trackingDisposable.DisposeWith(_disposables);
        _ = InitializeAsync();

        this.WhenAnyValue(x => x.IsTracking)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SelectMany(on => on
                ? Observable.FromAsync(StartTrackingCore)
                : Observable.FromAsync(StopTrackingCore))
            .Subscribe()
            .DisposeWith(_disposables);

        LogStep("Reactive subscriptions setup done");

        _logger.LogInformation("[Init] MainViewModel ctor finished");
    }
    private void LogStep(string step)
    {
        _sw.Stop();
        _logger.LogInformation("[Init] +{Elapsed}ms - {Step}",
            _sw.ElapsedMilliseconds,
            step);

        _sw.Restart();
    }
    public void Dispose()
    {
        _ = _workerConnection.DisposeAsync();
        _logger.LogDebug("Dispose ElementTracker");
        GC.SuppressFinalize(this);
        _audioPlayer.Dispose();
        _tsService.Dispose();
        _disposables.Dispose();
    }

    public async Task InitializeAsync()
    {
        await _workerConnection.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        _workerConnection.State
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(s => WorkerState = s)
            .DisposeWith(_disposables);

        _workerConnection
            .PollStatus(TimeSpan.FromSeconds(5))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(status =>
            {
                _logger.LogDebug($"StatusCheck:{status}");
                if (status is not null)
                    StatusText = $"v{status.Version}  queries={status.QueriesServed}  " +
                                 $"cache={status.CacheHits}/{status.CacheHits + status.CacheMisses}";
            })
            .DisposeWith(_disposables);

        await _tsService.EnsureStartedAsync();
        // CancellationTokenSource for aborting in-flight TTS requests
        this.WhenValueChanged(vm => vm.CurrentElement)
                    .DistinctUntilChanged()
                    .Throttle(TimeSpan.FromMilliseconds(300))
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Select(info => Observable.FromAsync(async ct =>
                    {
                        try
                        {
                            await _highlightService.StartBreathAsync(CancellationToken.None);
                            await SpeakElementAsync(info, ct);
                        }
                        finally
                        {
                            await _highlightService.StopBreathAsync();
                        }
                    }))
                    .Switch()
                    .Subscribe(
                        onNext: _ => { },
                        onError: ex => _logger.LogError(ex, "TTS pipeline error")
                    );
    }
    /// <summary>
    /// Cancels any in-flight TTS request and speaks the new element.
    /// </summary>
    private async Task SpeakElementAsync(ElementInfo? info, CancellationToken ct)
    {
        if (info is null) return;

        _logger.LogDebug("Get element: {Name}; rect: {Rect}", info.Name, info.Rect);

        var oldCts = _ttsCts;
        _ttsCts = new CancellationTokenSource();
        await oldCts.CancelAsync();
        oldCts.Dispose();

        try
        {
            var voiceBytes =
            await _tsService.GetVoiceAsync(
                $"This is {info.Name}",
                ct: _ttsCts.Token);

            _audioPlayer.Stop();
            await _audioPlayer.PlayBytesAsync(voiceBytes);
        }
        catch (OperationCanceledException oce)
        {
            _logger.LogError(oce, "SpeakAsync");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpeakAsync failed for element: {Name}", info.Name);
        }
    }
    private Task StartTrackingCore()
    {
        if (_trackingDisposable.Disposable is not null &&
            _trackingDisposable.Disposable != Disposable.Empty)
        {
            _mouseTracker.Resume();
            _highlightService.Resume();
            return Task.CompletedTask;
        }

        var isFirst = true;
        _trackingDisposable.Disposable = _workerConnection
            .TrackMouseElement(
                _mouseTracker.MousePositionStream.Select(p => (p.X, p.Y)),
                throttle: TimeSpan.FromMilliseconds(10))
            .WhereNotNull()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Do(info =>
            {
                if (isFirst) { _highlightService.Initialize(); isFirst = false; }
            })
            .Subscribe(info =>
            {
                CurrentElement = info;
                _highlightService.UpdateLocation(new System.Drawing.Rectangle
                {
                    X = info.Rect.Left,
                    Y = info.Rect.Top,
                    Width = info.Rect.Width,
                    Height = info.Rect.Height
                });
            });

        /// disable mouse select monitor for now
        //_mouseTracker.SelectedTextStream.Subscribe(async t =>
        //{
        //    var voiceBytes =
        //    await _tsService.GetVoiceAsync(
        //        $"Selected text is {t}");

        //    _audioPlayer.Stop();
        //    await _audioPlayer.PlayBytesAsync(voiceBytes);
        //});
        return Task.CompletedTask;
    }

    private Task StopTrackingCore()
    {
        _mouseTracker.Pause();
        CurrentElement = null;
        _highlightService.Hide();
        return Task.CompletedTask;
    }
}
