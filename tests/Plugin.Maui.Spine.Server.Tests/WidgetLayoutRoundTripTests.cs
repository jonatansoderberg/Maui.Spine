using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// The layout has to survive the trip out to a device and back into a tree: on Android a Live Update
/// arrives as serialized layout in a data message and is rendered in the app's process.
/// </summary>
public class WidgetLayoutRoundTripTests
{
    private static readonly WidgetColor Brand = WidgetColor.FromHex("#1B5E3F");

    [Fact]
    public void A_styled_tree_comes_back_with_its_styling()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(4,
                W.Text("Gävle OK").Headline().Bold(),
                W.Text("Ute på banan").Caption().Secondary()),
        };

        var back = WidgetJson.DeserializeLayout(layout.ToJson());

        var stack = Assert.IsType<VStackNode>(back!.LockScreen);
        var first = Assert.IsType<TextNode>(stack.Children[0]);
        var second = Assert.IsType<TextNode>(stack.Children[1]);

        Assert.Equal("Gävle OK", first.Text);
        Assert.Equal(TextRole.Headline, first.Role);
        Assert.True(first.Bold);
        Assert.Equal(TextRole.Caption, second.Role);
        Assert.Equal(WidgetColor.Secondary, second.Color);
    }

    [Fact]
    public void Every_region_and_node_kind_survives()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.HStack(8, W.Icon("fish", Brand), W.Spacer(), W.Divider()),
            ExpandedLeading = W.Icon("bell"),
            ExpandedTrailing = W.Timer(new DateTimeOffset(2026, 9, 12, 10, 24, 0, TimeSpan.Zero)).Bold(),
            ExpandedCenter = W.Text("Mitten"),
            ExpandedBottom = W.Progress(0.4, Brand),
            CompactLeading = W.Icon("clock"),
            CompactTrailing = W.Text("10:24").Caption(),
            Minimal = W.Icon("house"),
        };

        var json = layout.ToJson();

        Assert.Equal(json, WidgetJson.DeserializeLayout(json)!.ToJson());
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("secondary")]
    [InlineData("accent")]
    [InlineData("green")]
    [InlineData("#1B5E3F")]
    public void Colors_read_back_to_what_they_were_written_as(string value)
    {
        Assert.Equal(value, WidgetColor.Parse(value).Value);
    }

    [Fact]
    public void A_colour_that_is_neither_a_name_nor_hex_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WidgetColor.Parse("chartreuse"));
    }
}
