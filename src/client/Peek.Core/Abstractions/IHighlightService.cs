using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IHighlightService
{
    void Initialize();
    void Show(Rectangle rect);
    void Hide();
    void Resume();
    void Reset();
    void UpdateLocation(Rectangle rect);
    Task UpdateLocationAsync(Rectangle rect);
    void Clear();
    void UpdateColorsRandomly();
    Task StartBreathAsync(CancellationToken cancellationToken);
    Task StopBreathAsync();
}
