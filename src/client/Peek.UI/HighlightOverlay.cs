using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.UI.Views;
using Pipboy.Avalonia;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Peek.UI;

public class HighlightOverlay : IHighlightOverlay
{
    private readonly Dispatcher _dispatcher;
    private HighlightBorder? _window;
    private readonly ILogger _logger;

    public HighlightOverlay(PipboyThemeManager pipboyThemeManager, 
        ILogger<HighlightOverlay> logger)
    {
        _dispatcher = Application.Current!.Dispatcher;
        _logger = logger;
        pipboyThemeManager.ThemeColorChanged += PipboyThemeManager_ThemeColorChanged;
    }

    private void PipboyThemeManager_ThemeColorChanged(object? sender, ThemeColorChangedEventArgs e)
    {
        SetFillColor(e.Palette.Primary.WithOpacity(0.2).ToString());
        SetBeamEffectColor(e.Palette.Primary.ToString());
        SetBorderColor(e.Palette.Primary.ToString());
    }

    public void Show(Rectangle rect)
    {
        _dispatcher.Invoke(() =>
        {
            if (_window == null)
            {
                _window = new HighlightBorder();
                _window.Show();
            }
            _window.Show();
            Reposition(rect);
        });
    }

    public void Hide()
    {
        _dispatcher.Invoke(() => _window?.Hide());
    }

    public void Close()
    {
        _dispatcher.Invoke(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    public void Update(Rectangle rect)
    {
        _dispatcher.Invoke(() =>
        {
            Reposition(rect);
        });
    }

    public void Reset(Rectangle rect)
    {
        Update(rect);
    }

    public void SetBorderColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetBorderBrush(new SolidColorBrush(Avalonia.Media.Color.Parse(color)));
        });
    }

    public void SetFillColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetFillBrush(new SolidColorBrush(Avalonia.Media.Color.Parse(color)));
        });
    }
    public void SetBeamEffectColor(string color)
    {
        if (_window == null) return;

        _dispatcher.Invoke(() =>
        {
            _window.ResetBeamEffectColor(Avalonia.Media.Color.Parse(color));
        });
    }
    public nint GetNativeHandle()
    {
        var handle = _window?.TryGetPlatformHandle()?.Handle;
        return handle ?? throw new InvalidOperationException("No window handle");
    }

    private void Reposition(Rectangle rect)
    {
        if (_window == null) return;

        var scaling = _window.Screens.Primary?.Scaling ?? 1.0;

        _window.Position = new PixelPoint(
            (int)(rect.X / scaling),
            (int)(rect.Y / scaling));


        _window.Width = rect.Width / scaling;
        _window.Height = rect.Height / scaling;
    }

    public void Dispose()
    {
        Close();
    }

    public async Task StartSpeakingAsync(CancellationToken cancellationToken)
    {
        if (_window is not null)
        {
            await _window.StartBreathAnimationAsync(cancellationToken);
        }
    }

    public async Task StopSpeakingAsync()
    {
        if (_window is not null)
        {
            await _window.StopBreathAnimationAsync();
        }
    }
}