namespace Plugin.Maui.Spine.Common;

/// <summary>Walks a widget tree through every node that holds others.</summary>
internal static class WidgetTree
{
    /// <summary><paramref name="node"/> and every node under it, in tree order.</summary>
    public static IEnumerable<WidgetNode> Nodes(WidgetNode? node) => node switch
    {
        null => [],
        StackNode stack => stack.Children.SelectMany(Nodes).Prepend(stack),
        ButtonNode button => Nodes(button.Child).Prepend(button),
        AdaptiveNode adaptive => Nodes(adaptive.Fallback).Concat(adaptive.Trees.Values.SelectMany(Nodes)).Prepend(adaptive),
        _ => [node],
    };

    /// <summary>The symbol name of every <see cref="IconNode"/> in <paramref name="node"/>, in tree order, repeats included.</summary>
    public static IEnumerable<string> Icons(WidgetNode? node) =>
        Nodes(node).OfType<IconNode>().Where(icon => !string.IsNullOrWhiteSpace(icon.SystemName)).Select(icon => icon.SystemName);
}
