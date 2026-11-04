using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>An app's own light and dark colors (#752), written as one string an older app reads as primary.</summary>
public class WidgetColorPairTests
{
    [Fact]
    public void A_pair_is_written_as_light_slash_dark()
    {
        var ink = WidgetColor.Pair("#1c1c1e", "FFFFFF");
        Assert.Equal("#1C1C1E/#FFFFFF", ink.Value);
        var json = new LiveActivityLayout { LockScreen = W.Label("a").TextColor(ink) }.ToJson();
        Assert.Contains("\"color\":\"#1C1C1E/#FFFFFF\"", json);
        Assert.Equal(ink, Assert.IsType<TextNode>(WidgetJson.DeserializeLayout(json)!.LockScreen).Color);
    }

    [Fact]
    public void Each_half_must_be_a_hex_color()
    {
        Assert.Throws<ArgumentException>(() => WidgetColor.Pair("primary", "#FFFFFF"));
        Assert.Throws<ArgumentException>(() => WidgetColor.Parse("#FFFFFF/nope"));
        Assert.Equal(WidgetColor.Pair("#000000", "#FFFFFF"), WidgetColor.Parse("#000000/#FFFFFF"));
    }
}
