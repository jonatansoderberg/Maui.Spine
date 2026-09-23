using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// The app rasterizes every icon the walk finds before a tree is written; an icon it misses is left out
/// on Android and drawn as the SF Symbol of the same name on iOS.
/// </summary>
public class WidgetTreeTests
{
    [Fact]
    public void Icons_are_found_inside_buttons_and_adaptive_nodes()
    {
        var tree = W.VStack(
            W.Icon("in.stack"),
            W.Button("next", W.Icon("in.button")),
            W.Adaptive(
                W.HStack(W.Icon("in.fallback")),
                new Dictionary<WidgetFamily, WidgetNode> { [WidgetFamily.Small] = W.Button("play", W.VStack(W.Icon("in.family"))) }));

        Assert.Equal(["in.stack", "in.button", "in.fallback", "in.family"], WidgetTree.Icons(tree));
    }

    [Fact]
    public void A_tree_without_icons_yields_none()
    {
        Assert.Empty(WidgetTree.Icons(W.VStack(W.Text("Hej"), W.Spacer())));
        Assert.Empty(WidgetTree.Icons(null));
    }
}
