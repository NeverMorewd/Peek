using System.Drawing;
using System.Reactive;

namespace Peek.Core.Abstractions;

public interface IMouseTracker
{
    public IObservable<Point> MousePositionStream { get; }
    public IObservable<Unit> SelectedStream { get; }
    public void Pause();
    public void Resume();
}
