using Peek.Core.Abstractions;
using System.Drawing;

namespace Peek.Core.Services;

public class HighlightService : IHighlightService
{
    private readonly IHighlightOverlay _overlay;
    private bool _isVisible;
    private bool _isReady;
    private Rectangle _currentRect;

    public HighlightService(IHighlightOverlay overlay)
    {
        _overlay = overlay;
    }

    public void Initialize()
    {
        if (_isReady) return;

        Show(new Rectangle(-100, -100, 0, 0));
        Resume();
        _isReady = true;
    }

    public void Show(Rectangle rect)
    {
        _overlay.Show(rect);
        _currentRect = rect;
        _isVisible = true;
    }

    public void Hide()
    {
        if (!_isVisible) return;

        _overlay.Hide();
        _isVisible = false;
    }

    public void Resume()
    {
        if (_isVisible) return;

        _overlay.Show(_currentRect);
        _isVisible = true;
    }

    public void Reset()
    {
        UpdateLocation(new Rectangle(-100, -100, 0, 0));
    }

    public void UpdateLocation(Rectangle rect)
    {
        _currentRect = rect;
        _overlay.Update(rect);
    }

    public async Task UpdateLocationAsync(Rectangle rect)
    {
        _currentRect = rect;
        await Task.Run(() => _overlay.Update(rect));
    }

    public void Clear()
    {
        _overlay.Close();
        _isReady = false;
        _isVisible = false;
    }

    public void UpdateColorsRandomly()
    {
        var rand = new Random();

        var color = System.Drawing.Color.FromArgb(
            rand.Next(50, 255),
            rand.Next(50, 255),
            rand.Next(50, 255));

        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var fill = $"#30{color.R:X2}{color.G:X2}{color.B:X2}";

        _overlay.SetBorderColor(hex);
        _overlay.SetFillColor(fill);
    }

    public Task StartBreathAsync(CancellationToken cancellationToken)
    {
        return _overlay.StartSpeakingAsync(cancellationToken);
    }

    public Task StopBreathAsync()
    {
        return _overlay.StopSpeakingAsync();
    }
}