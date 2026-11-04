using System.ComponentModel;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// What the code generated from a <c>*.layout.xml</c> calls: the few steps the <see cref="W"/> builders do not take
/// themselves. Not meant to be called by hand.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LayoutValues
{
    /// <summary>A bound color as it is.</summary>
    public static WidgetColor Color(WidgetColor color) => color;

    /// <summary>A bound color that may be absent.</summary>
    public static WidgetColor? Color(WidgetColor? color) => color;

    /// <summary>A bound color written as text: a semantic name or a hex value (<c>GlucoseColors.High</c>).</summary>
    public static WidgetColor Color(string color) => WidgetColor.Parse(color);

    /// <summary>Changes <paramref name="node"/> only when <paramref name="condition"/> holds: an attribute bound to a value that may be absent.</summary>
    public static T When<T>(T node, bool condition, Func<T, T> change) where T : WidgetNode => condition ? change(node) : node;

    /// <summary>Adds a shared node unless a condition left it out.</summary>
    public static void Share(Dictionary<string, WidgetNode> shared, string name, WidgetNode? node)
    {
        if (node is not null)
            shared[name] = node;
    }

    /// <summary>
    /// A <c>ForEach</c>: the nodes of each of at most <paramref name="max"/> items, or <paramref name="empty"/> when there
    /// are none.
    /// </summary>
    public static IEnumerable<WidgetNode?> Each<T>(IEnumerable<T>? items, int? max, Func<T, IEnumerable<WidgetNode?>> item,
        IEnumerable<WidgetNode?>? empty = null)
    {
        var any = false;
        foreach (var value in max is { } count ? (items ?? []).Take(count) : items ?? [])
        {
            any = true;
            foreach (var node in item(value))
                yield return node;
        }
        if (!any && empty is not null)
            foreach (var node in empty)
                yield return node;
    }
}
