using AsyncNavigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Models;
using Peek.Core.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Peek.Core.ViewModels;

public partial class WindowTrackViewModel : ViewModelBase, IDisposable
{
    private readonly WindowTracker _tracker;
    private readonly ILogger<WindowTrackViewModel> _logger;
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<nint, WindowItemViewModel> _index = [];
    private readonly BehaviorSubject<bool> _liveMonitoringSubject = new(true);
    public ObservableCollection<WindowItemViewModel> Roots { get; } = [];

    [Reactive] 
    public bool _isLiveMonitoring;
    [Reactive] 
    public string? _searchText;
    [Reactive] 
    public bool _showOnlyVisible;
    [Reactive] 
    public bool _showChildren;
    [Reactive] 
    public bool _hideToolWindows;
    [Reactive]
    public bool _isRefreshing;

    [Reactive]
    public IReadOnlySet<uint> _excludePids =
        new HashSet<uint> { (uint)Environment.ProcessId };

    [Reactive]
    public WindowItemViewModel? _selectedWindow;
    [Reactive]
    public int _totalCount;
    [Reactive]
    public int _filteredCount;
    public ReactiveCommand<Unit, Unit> RefreshCommand 
    { 
        get; 
    }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand 
    { 
        get; 
    }
    public bool ExcludeSelf
    {
        get => ExcludePids.Contains((uint)Environment.ProcessId);
        set
        {
            var pid = (uint)Environment.ProcessId;
            var set = new HashSet<uint>(ExcludePids);
            if (value) set.Add(pid); else set.Remove(pid);
            ExcludePids = set;
            this.RaisePropertyChanged();
        }
    }

    public WindowTrackViewModel(WindowTracker tracker,
        IServiceProvider serviceProvider,
        ILogger<WindowTrackViewModel> logger)
    {
        _tracker = tracker;
        _logger = logger;
        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await LoadSnapshotAsync();
        });

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        var disposeService = serviceProvider.GetRequiredService<IDisposeService>();
        disposeService.Register(this);



    }
    public async override Task OnNavigatedToAsync(NavigationContext context)
    {
        await base.OnNavigatedToAsync(context);    
    }
    public override async Task InitializeAsync(NavigationContext context)
    {
        await base.InitializeAsync(context);
        this.WhenAnyValue(x => x.IsLiveMonitoring)
            .Subscribe(v =>
            {
                _liveMonitoringSubject.OnNext(v);
                _logger.LogInformation("Live monitoring: {State}", v ? "ON" : "OFF");
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(
                x => x.SearchText,
                x => x.ShowOnlyVisible,
                x => x.ShowChildren,
                x => x.HideToolWindows,
                x => x.ExcludePids,
                x => x.ExcludeSelf)
            .Throttle(TimeSpan.FromMilliseconds(150), RxSchedulers.TaskpoolScheduler)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe((_) => RebuildTree())
            .DisposeWith(_disposables);

        await LoadSnapshotAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(1000));
    }

    private async Task LoadSnapshotAsync()
    {
        IsRefreshing = true;
        _logger.LogDebug("Loading snapshot in background...");

        await Task.Delay(100);

        var nodes = await Task.Run(() => _tracker.EnumerateAll());

        var newIndex = await Task.Run(() =>
            nodes.ToDictionary(n => n.Hwnd, n => new WindowItemViewModel(n)));

        _index.Clear();
        foreach (var (hwnd, vm) in newIndex)
            _index[hwnd] = vm;

        TotalCount = _index.Count;

        _logger.LogDebug("Snapshot loaded: {Count} windows", TotalCount);

        RebuildTree();
        IsRefreshing = false;
    }

    private void ApplyChanges(IReadOnlyList<WindowChange> changes)
    {
        if (changes.Count == 0) return;

        bool needsRebuild = false;

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case WindowChangeKind.Added:
                    _index[change.Node.Hwnd] = new WindowItemViewModel(change.Node);
                    needsRebuild = true;
                    break;

                case WindowChangeKind.Removed:
                    _index.Remove(change.Node.Hwnd);
                    needsRebuild = true;
                    break;

                case WindowChangeKind.ParentChanged:
                    if (_index.TryGetValue(change.Node.Hwnd, out var rp))
                        rp.ApplyUpdate(change.Node);
                    needsRebuild = true;
                    break;

                default:
                    // In-place update only — no structural change
                    if (_index.TryGetValue(change.Node.Hwnd, out var vm))
                        vm.ApplyUpdate(change.Node);
                    break;
            }
        }

        TotalCount = _index.Count;
        if (needsRebuild) RebuildTree();
    }

    private void RebuildTree()
    {
        var options = new WindowTreeOptions
        {
            HideToolWindows = HideToolWindows,
            HideInvisible = ShowOnlyVisible,
            ExcludePids = ExcludePids,
        };

        var flat = _index.Values.Select(vm => vm.Node).ToList();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText;
            flat = [.. flat.Where(n =>
                n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.ClassName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                $"0x{n.Hwnd:X}".Contains(q, StringComparison.OrdinalIgnoreCase)
            )];
        }

        var treeRoots = WindowTreeBuilder.BuildTree(flat, options);
        SyncRootsCollection(treeRoots);
        FilteredCount = CountAll(treeRoots);
    }

    private void SyncRootsCollection(IReadOnlyList<WindowNode> treeRoots)
    {
        var newSet = treeRoots.Select(n => n.Hwnd).ToHashSet();

        for (int i = Roots.Count - 1; i >= 0; i--)
            if (!newSet.Contains(Roots[i].Node.Hwnd))
                Roots.RemoveAt(i);

        for (int i = 0; i < treeRoots.Count; i++)
        {
            var node = treeRoots[i];
            if (!_index.TryGetValue(node.Hwnd, out var vm)) continue;

            SyncChildren(vm, node.Children);

            if (i < Roots.Count && Roots[i].Node.Hwnd == node.Hwnd) continue;

            var existing = Roots.FirstOrDefault(r => r.Node.Hwnd == node.Hwnd);
            if (existing is not null)
                Roots.Move(Roots.IndexOf(existing), i);
            else
                Roots.Insert(Math.Min(i, Roots.Count), vm);
        }
    }

    private void SyncChildren(WindowItemViewModel parent, IReadOnlyList<WindowNode> children)
    {
        var newSet = children.Select(n => n.Hwnd).ToHashSet();

        for (int i = parent.Children.Count - 1; i >= 0; i--)
            if (!newSet.Contains(parent.Children[i].Node.Hwnd))
                parent.Children.RemoveAt(i);

        for (int i = 0; i < children.Count; i++)
        {
            var node = children[i];
            if (!_index.TryGetValue(node.Hwnd, out var childVm)) continue;

            SyncChildren(childVm, node.Children);

            if (i < parent.Children.Count && parent.Children[i].Node.Hwnd == node.Hwnd) continue;

            var existing = parent.Children.FirstOrDefault(c => c.Node.Hwnd == node.Hwnd);
            if (existing is not null)
            {
                var idx = parent.Children.IndexOf(existing);
                if (idx != i) parent.Children.Move(idx, i);
            }
            else
            {
                parent.Children.Insert(Math.Min(i, parent.Children.Count), childVm);
            }
        }
    }

    private static int CountAll(IReadOnlyList<WindowNode> nodes)
    {
        int count = nodes.Count;
        foreach (var n in nodes) count += CountAll(n.Children);
        return count;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _liveMonitoringSubject.Dispose();
    }
}
