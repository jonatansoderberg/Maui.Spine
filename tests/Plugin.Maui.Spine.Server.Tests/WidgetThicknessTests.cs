using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>Padding per side and margin (#744), written so an older renderer still reads the layout.</summary>
public class WidgetThicknessTests
{
    static string Json(WidgetNode tree) => new LiveActivityLayout { LockScreen = tree }.ToJson();

    [Fact]
    public void Uniform_padding_is_written_as_before()
    {
        var json = Json(W.VStack(W.Text("a")).Padding(16));
        Assert.Contains("\"padding\":16", json);
        Assert.DoesNotContain("insets", json);
        Assert.Contains("\"padding\":16", Json(W.VStack(W.Text("a")).Padding(16, 16)));
    }

    [Fact]
    public void Uneven_padding_goes_in_insets_with_the_smallest_side_as_padding()
    {
        var json = Json(W.VStack(W.Text("a")).Padding(16, 10, 16, 6));
        Assert.Contains("\"padding\":6", json);
        Assert.Contains("\"insets\":[16,10,16,6]", json);

        var back = Assert.IsType<VStackNode>(WidgetJson.DeserializeLayout(json)!.LockScreen);
        Assert.Equal(new WidgetThickness(16, 10, 16, 6), back.Insets);
    }

    [Fact]
    public void Horizontal_and_vertical_padding_fill_both_sides_of_each()
    {
        var stack = W.HStack(W.Text("a")).Padding(10, 6);
        Assert.Equal(new WidgetThickness(10, 6, 10, 6), stack.Insets);
        Assert.Equal(6, stack.Padding);
    }

    [Fact]
    public void A_uniform_padding_replaces_uneven_insets()
    {
        var stack = W.VStack(W.Text("a")).Padding(16, 10).Padding(4);
        Assert.Null(stack.Insets);
        Assert.Equal(4, stack.Padding);
    }

    [Fact]
    public void Any_node_takes_a_margin()
    {
        var json = Json(W.VStack(W.Text("a").Margin(0, 4), W.Icon("drop").Margin(2)));
        Assert.Contains("\"margin\":[0,4,0,4]", json);
        Assert.Contains("\"margin\":[2,2,2,2]", json);

        var back = Assert.IsType<VStackNode>(WidgetJson.DeserializeLayout(json)!.LockScreen);
        Assert.Equal(new WidgetThickness(0, 4, 0, 4), back.Children[0].Margin);
    }

    [Fact]
    public void Negative_sides_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => W.VStack().Padding(4, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => W.Text("a").Margin(-2));
    }
}
