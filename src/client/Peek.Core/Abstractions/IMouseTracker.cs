using System.Drawing;

namespace Peek.Core.Abstractions;

public interface IMouseTracker
{
    public IObservable<Point> MousePositionStream { get; }
    public IObservable<string> SelectedTextStream { get; }
    public void Pause();
    public void Resume();
}
