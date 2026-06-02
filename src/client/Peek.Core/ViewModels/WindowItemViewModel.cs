// ViewModels/WindowItemViewModel.cs
// Per-row ViewModel for the TreeDataGrid.

using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Peek.Core.Models;

namespace Peek.Core.ViewModels;

public partial  class WindowItemViewModel : ReactiveObject
{
    private WindowNode _node;
    public  WindowNode  Node => _node;


    [Reactive]
    public string _title;
    [Reactive]
    public string _className;
    [Reactive]
    public string _processName;
    [Reactive]
    public uint _processId;
    [Reactive] 
    public uint _threadId;
    [Reactive] 
    public bool _isVisible;
    [Reactive] 
    public bool _isEnabled;
    [Reactive] 
    public bool _isMinimized;
    [Reactive] 
    public bool _isMaximized;
    [Reactive] 
    public bool _isTopmost;
    [Reactive] 
    public string _rectDisplay;
    [Reactive] 
    public string _styleHex;
    [Reactive] 
    public string _exStyleHex;
    [Reactive] 
    public bool _isExpanded;

    public string HwndDisplay => $"0x{_node.Hwnd:X8}";


    public ObservableCollection<WindowItemViewModel> Children { get; } = new();


    [Reactive] public bool _isHighlighted ;
    private IDisposable? _highlightTimer;


    public WindowItemViewModel(WindowNode node)
    {
        _node       = node;
        Title       = node.Title;
        ClassName   = node.ClassName;
        ProcessName = node.ProcessName;
        ProcessId   = node.ProcessId;
        ThreadId    = node.ThreadId;
        IsVisible   = node.IsVisible;
        IsEnabled   = node.IsEnabled;
        IsMinimized = node.IsMinimized;
        IsMaximized = node.IsMaximized;
        IsTopmost   = node.IsTopmost;
        RectDisplay = FormatRect(node.Rect);
        StyleHex    = $"0x{node.Style:X8}";
        ExStyleHex  = $"0x{node.ExStyle:X8}";
    }

    public void ApplyUpdate(WindowNode fresh)
    {
        bool changed = false;

        void Set<T>(T freshVal, T oldVal, Action<T> setter)
        {
            if (!EqualityComparer<T>.Default.Equals(freshVal, oldVal))
            { setter(freshVal); changed = true; }
        }

        Set(fresh.Title,       _node.Title,       v => Title       = v);
        Set(fresh.ClassName,   _node.ClassName,   v => ClassName   = v);
        Set(fresh.IsVisible,   _node.IsVisible,   v => IsVisible   = v);
        Set(fresh.IsEnabled,   _node.IsEnabled,   v => IsEnabled   = v);
        Set(fresh.IsMinimized, _node.IsMinimized, v => IsMinimized = v);
        Set(fresh.IsMaximized, _node.IsMaximized, v => IsMaximized = v);
        Set(fresh.IsTopmost,   _node.IsTopmost,   v => IsTopmost   = v);

        if (fresh.Rect != _node.Rect)
        { RectDisplay = FormatRect(fresh.Rect); changed = true; }

        if (fresh.Style != _node.Style)
        { StyleHex = $"0x{fresh.Style:X8}"; changed = true; }

        if (fresh.ExStyle != _node.ExStyle)
        { ExStyleHex = $"0x{fresh.ExStyle:X8}"; changed = true; }

        _node = fresh;
        if (changed) FlashHighlight();
    }

    private void FlashHighlight()
    {
        _highlightTimer?.Dispose();
        IsHighlighted   = true;
        _highlightTimer = Observable
            .Timer(TimeSpan.FromMilliseconds(800))
            .Subscribe(_ => IsHighlighted = false);
    }

    private static string FormatRect(WindowRect r) =>
        $"{r.Left},{r.Top}  {r.Width}×{r.Height}";

    public override string ToString() =>
        $"[0x{_node.Hwnd:X}] {_node.Title} ({_node.ClassName})";
}
