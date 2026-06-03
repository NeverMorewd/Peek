using AsyncNavigation;
using AsyncNavigation.Abstractions;
using AsyncNavigation.Core;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services;
using Peek.Core.ViewModels;
using Peek.Ipc.DependencyInjection;
using Peek.UI.Indicators;
using Peek.UI.Views;
using Peek.Views;
using Pipboy.Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Peek.UI;

[SupportedOSPlatform("windows7.0")]
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        //AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        //TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        NavigationOptions navigationOptions = new()
        {
            /// default is CancelCurrent <see cref="NavigationJobStrategy.CancelCurrent"/>
            NavigationJobStrategy = NavigationJobStrategy.CancelCurrent,
            LoadingIndicatorDelay = TimeSpan.FromMilliseconds(1)
        };
        var services = new ServiceCollection();
        services.AddNavigationSupport(navigationOptions)
                .RegisterDialogWindow<SplashWindow, SplashViewModel>("SplashWindow")
                .RegisterView<ElementTrackView, ElementTrackViewModel>(nameof(ElementTrackView))
                .RegisterInnerIndicatorProvider<ProgressIndicatorProvider>()
                .AddSingleton<MainWindow>()
                .AddSingleton<ElementTracker>()
                .AddTransient<WindowTracker>()
                .AddSingleton<IDisposeService, DisposeService>()
                .AddTransient<LoadingIndicatorView>()
                .AddSingleton<WindowsHookService>()
                .AddSingleton<AudioPlayer>()
                .AddSingleton<IColorChangedNotify, MainWindow>(sp => sp.GetRequiredService<MainWindow>())
                .RegisterNavigation<WindowTrackView, WindowTrackViewModel>(nameof(WindowTrackView))
                .AddSingleton<TtsService>()
                .AddSingleton<IClipboardService, AvaloniaClipboardProvider>()
                .AddSingleton<ColorPickerViewModel>()
                .AddSingleton<WindowEnumerator>()
                .AddSingletonWithAllMembers<MainViewModel>()
                .AddSingleton<IHighlightOverlay, HighlightOverlay>()
                .AddSingleton<IHighlightService, HighlightService>()
                .AddSingleton(PipboyThemeManager.Instance)
                .AddLogging(builder =>
                  {
                      builder.ClearProviders();

                      builder.AddSimpleConsole(options =>
                      {
                          options.IncludeScopes = true;
                          options.SingleLine = true;
                          options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                          options.UseUtcTimestamp = false;
                          options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                      });

                      builder.SetMinimumLevel(LogLevel.Debug);
                  })
                .AddPeekIpc();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416 // Validate platform compatibility
            services.AddTransient<IMouseTracker, WindowsMouseTracker>();
#pragma warning restore CA1416 // Validate platform compatibility
        }
        else
        {
            throw new NotSupportedException($"Unsupported OS:{RuntimeInformation.OSDescription}");
        }

        var sp = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = sp.GetRequiredService<MainWindow>();
            mainWindow.DataContext = sp.GetRequiredService<MainViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnExit;
            //var dialogService = sp.GetRequiredService<IDialogService>();
            //dialogService.FrontShowWindowAsync("SplashWindow", result =>
            //{
            //    if (result.Result == DialogButtonResult.Done)
            //    {
            //        var mainWindow = sp.GetRequiredService<MainWindow>();
            //        mainWindow.DataContext = sp.GetRequiredService<MainViewModel>();
            //        desktop.MainWindow = mainWindow;
            //        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            //        desktop.Exit += OnExit;
            //        return mainWindow;
            //    }
            //    else
            //    {
            //        if (Current?.ApplicationLifetime is IControlledApplicationLifetime applicationLifetime)
            //        {
            //            applicationLifetime.Shutdown();
            //        }
            //        return null;
            //    }
            //});

        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = sp.GetRequiredService<MainViewModel>() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = sp.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Console.WriteLine(e.Exception);
    }

    private void Dispatcher_UnhandledException(object sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Console.WriteLine(e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Console.WriteLine(e.ExceptionObject);
    }

    void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = desktop.MainWindow?.DataContext as IDisposable;
            vm?.Dispose();
        }
    }
}