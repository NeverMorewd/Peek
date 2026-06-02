// Services/WindowTreeBuilder.cs
// Converts a flat List<WindowNode> into a hierarchical tree.

using Peek.Core.Models;

namespace Peek.Core.Services;

public static class WindowTreeBuilder
{
    public static IReadOnlyList<WindowNode> BuildTree(
        IReadOnlyList<WindowNode> flat,
        WindowTreeOptions? options = null)
    {
        options ??= WindowTreeOptions.Default;

        var byHwnd          = flat.ToDictionary(n => n.Hwnd);
        var childrenByParent = flat
            .GroupBy(n => n.ParentHwnd)
            .ToDictionary(g => g.Key, g => g.ToList());
        var roots = flat
            .Where(n => !byHwnd.ContainsKey(n.ParentHwnd))
            .Where(n => !options.HideToolWindows || !n.IsToolWindow)
            .Where(n => !options.HideInvisible   ||  n.IsVisible)
            .Where(n => !options.ExcludePids.Contains(n.ProcessId))
            .OrderBy(n => n.Hwnd)
            .Select(n => AttachChildren(n, childrenByParent, options, depth: 0))
            .ToList();

        return roots;
    }

    private static WindowNode AttachChildren(
        WindowNode node,
        Dictionary<nint, List<WindowNode>> childrenByParent,
        WindowTreeOptions options,
        int depth)
    {
        if (!childrenByParent.TryGetValue(node.Hwnd, out var raw) || raw.Count == 0)
            return node with { Depth = depth };

        var children = raw
            .Where(n => !options.HideInvisible || n.IsVisible)
            .OrderBy(n => n.Hwnd)
            .Select(n => AttachChildren(n, childrenByParent, options, depth + 1))
            .ToArray();

        return node with { Children = children, Depth = depth };
    }
}

public sealed class WindowTreeOptions
{
    public static readonly WindowTreeOptions Default = new();

    public bool HideToolWindows { get; init; } = false;
    public bool HideInvisible   { get; init; } = false;
    public IReadOnlySet<uint> ExcludePids { get; init; } = new HashSet<uint>();
}
