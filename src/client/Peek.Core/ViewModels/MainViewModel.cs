using AsyncNavigation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using ReactiveUI;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.SourceGenerators;
using ReactiveUI.Primitives;

namespace Peek.Core.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MultipleDisposable _disposables = [];
    private readonly ILogger _logger;
    private readonly IRegionManager _regionManager;
    private readonly IViewManager _viewManager;
    private readonly IDisposeService _disposeService;
    private readonly IMouseTracker _mouseTracker;
    private readonly IDialogService _dialogService;

    [Reactive]
    private ColorPickerViewModel _titleBarContext;
    [Reactive]
    private string? _currentViewName;
    public MainViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger)
    {
        _logger = logger;
        _titleBarContext = serviceProvider.GetRequiredService<ColorPickerViewModel>();
        _regionManager = serviceProvider.GetRequiredService<IRegionManager>();
        _viewManager = serviceProvider.GetRequiredService<IViewManager>();
        _mouseTracker = serviceProvider.GetRequiredService<IMouseTracker>();
        _dialogService = serviceProvider.GetRequiredService<IDialogService>();

        _disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        _ = serviceProvider.GetRequiredService<AudioPlayer>().VlcInitializeAsync();

        _logger.LogInformation("[Init] MainViewModel ctor finished");

        if (_regionManager.TryGetRegion("MainRegion", out var region))
        {
            region.Navigated += (s, e) => 
            {
                CurrentViewName = e.Context.ViewName;
            };
        }
        _ = InitializeAsync();
    }
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _disposables?.Dispose();
        _viewManager.Clear();
        _disposeService.DisposeAll();
    }
    [ReactiveCommand]
    public async Task Navigation(string viewName)
    {
        await _regionManager.RequestNavigateAsync("MainRegion", viewName);
    }
    public async Task InitializeAsync()
    {

        var ret = await _regionManager.RequestNavigateAsync("MainRegion", "ElementTrackView");
        _mouseTracker.SelectedStream
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => 
            {
                
            });
    }
}
