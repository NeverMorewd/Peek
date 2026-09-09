using Irihi.Lingua;
using Microsoft.Extensions.DependencyInjection;
using Peek.Core.Models;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI.Primitives;

namespace Peek.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [Reactive]
    private LanguageItem _currentCulture;
    private readonly ILinguaManager _linguaManager;
    public IObservable<string?> DisplayLanguage => _linguaManager.GetObservable("Switch_Language");
    public SettingsViewModel(IServiceProvider serviceProvider)
    {
        _linguaManager = serviceProvider.GetRequiredService<ILinguaManager>();
        CurrentCulture = Languages.First();
        this.WhenAnyValue(x => x.CurrentCulture)
            .Where(culture => culture != null)
            .Subscribe(culture =>
            {
                _linguaManager.UpdateCulture(culture.Culture);
            });
    }

    public ObservableCollection<LanguageItem> Languages { get; } =
    [
        new("English", new CultureInfo("en-US")),
        new("中文",    new CultureInfo("zh-CN")),
        new("Deutsch", new CultureInfo("de-DE")),
    ];

}
