using System.Text.Json;
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
            LockScreen = W.HStack(8, W.Icon("fish", Brand), W.Spacer(), W.Divider()).Background(Brand).Padding(4).CornerRadius(6),
            ExpandedLeading = W.Icon("bell"),
            ExpandedTrailing = W.Timer(new DateTimeOffset(2026, 9, 12, 10, 24, 0, TimeSpan.Zero)).Bold(),
            ExpandedCenter = W.Text("Mitten"),
            ExpandedBottom = W.Progress(0.4, Brand),
            CompactLeading = W.Icon("clock"),
            CompactTrailing = W.Text("10:24").Caption(),
            Minimal = W.Icon("house"),
            Background = WidgetColor.Blue,
        };

        var json = layout.ToJson();

        Assert.Equal(json, WidgetJson.DeserializeLayout(json)!.ToJson());
    }

    [Fact]
    public void A_stack_keeps_its_box_and_the_layout_its_background()
    {
        var layout = new LiveActivityLayout
        {
            LockScreen = W.HStack(W.Text("Startad").Caption()).Background(Brand).Padding(6).CornerRadius(8),
            Background = WidgetColor.Blue,
        };

        var back = WidgetJson.DeserializeLayout(layout.ToJson())!;

        var stack = Assert.IsType<HStackNode>(back.LockScreen);
        Assert.Equal(Brand, stack.Background);
        Assert.Equal(6, stack.Padding);
        Assert.Equal(8, stack.CornerRadius);
        Assert.Equal(WidgetColor.Blue, back.Background);
    }

    [Fact]
    public void A_stack_without_a_box_writes_none_of_its_fields()
    {
        var json = new LiveActivityLayout { LockScreen = W.VStack(W.Text("Startad")) }.ToJson();

        Assert.DoesNotContain("padding", json);
        Assert.DoesNotContain("background", json);
        Assert.DoesNotContain("cornerRadius", json);
    }

    [Fact]
    public void A_timeline_carries_its_background_in_the_document()
    {
        using var document = JsonDocument.Parse(WidgetTimeline.Single(W.Text("Startad")).Background(Brand).ToJson());

        Assert.Equal("#1B5E3F", document.RootElement.GetProperty("background").GetString());
    }

    [Fact]
    public void Negative_padding_and_radius_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => W.VStack().Padding(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => W.VStack().CornerRadius(-1));
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
