namespace Plugin.Maui.Spine.Common;

/// <summary>Walks a widget tree through every node that holds others.</summary>
internal static class WidgetTree
{
    /// <summary>The symbol name of every <see cref="IconNode"/> in <paramref name="node"/>, in tree order, repeats included.</summary>
    public static IEnumerable<string> Icons(WidgetNode? node) => node switch
    {
        IconNode icon when !string.IsNullOrWhiteSpace(icon.SystemName) => [icon.SystemName],
        StackNode stack => stack.Children.SelectMany(Icons),
        ButtonNode button => Icons(button.Child),
        AdaptiveNode adaptive => Icons(adaptive.Fallback).Concat(adaptive.Trees.Values.SelectMany(Icons)),
        _ => [],
    };
}
