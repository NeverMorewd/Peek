using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Peek.Ipc.Connection;
using Peek.Ipc.Protocol;

namespace Peek.Ipc.Extensions;

public static class WorkerClientExtensions
{
    public static IObservable<ElementInfo?> TrackMouseElement(
        this WorkerConnection connection,
        IObservable<(int X, int Y)> mousePositions,
        TimeSpan? throttle    = null,
        IScheduler? scheduler = null)
    {
        var interval = throttle  ?? TimeSpan.FromMilliseconds(40);
        var sched    = scheduler ?? TaskPoolScheduler.Default;

        IObservable<ElementInfo?> QueryObservable() =>
            mousePositions
                .DistinctUntilChanged()
                .Throttle(interval, sched)
                .SelectMany(pos => Observable.FromAsync(ct =>
                    connection.Client.GetElementFromPointAsync(pos.X, pos.Y, ct)
                        .ContinueWith(
                            t => t.IsCompletedSuccessfully ? t.Result : null,
                            TaskContinuationOptions.ExecuteSynchronously)));

        return connection.State
            .Select(state => state == ConnectionState.Ready)
            .DistinctUntilChanged()
            .Select(isReady =>
                isReady
                    ? QueryObservable()
                    : Observable.Return<ElementInfo?>(null))
            .Switch();
    }
    public static IObservable<WorkerStatus?> PollStatus(
        this WorkerConnection connection,
        TimeSpan interval,
        IScheduler? scheduler = null)
    {
        var sched = scheduler ?? TaskPoolScheduler.Default;

        return Observable
            .Interval(interval, sched)
            .Where(_ => connection.CurrentState == ConnectionState.Ready)
            .SelectMany(_ => Observable.FromAsync(async ct =>
            {
                try
                {
                    return (WorkerStatus?)await connection.Client
                        .GetStatusAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }));
    }

    public static IObservable<bool> IsReady(this WorkerConnection connection) =>
        connection.State
            .Select(s => s == ConnectionState.Ready)
            .DistinctUntilChanged();
}
