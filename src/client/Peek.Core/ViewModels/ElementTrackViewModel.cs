using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services;

namespace Peek.Core.ViewModels;

public partial class ElementTrackViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger _logger;
    private readonly ElementTracker _elementTracker;
    public ElementTrackViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger)
    {
        _logger = logger;
        _elementTracker = serviceProvider.GetRequiredService<ElementTracker>();
        var disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        disposeService.Register(this);
    }
    public ElementTracker ElementTracker => _elementTracker;

    public void Dispose()
    {
        _elementTracker.Dispose();
    }
}
