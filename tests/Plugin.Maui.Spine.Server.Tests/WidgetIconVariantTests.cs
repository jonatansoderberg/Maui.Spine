using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>An icon drawn with heavier or round lines: written as a variant, stored under its name and that variant.</summary>
public class WidgetIconVariantTests
{
    [Fact]
    public void An_icon_as_it_stands_has_no_variant()
    {
        var icon = W.Icon("ArrowUpRight");

        Assert.Null(icon.Variant);
        Assert.Equal("ArrowUpRight", icon.Asset);
        Assert.DoesNotContain("variant", Json(icon));
    }

    [Theory]
    [InlineData(3, true, "3r")]
    [InlineData(1.5, false, "1.5")]
    [InlineData(1, true, "r")]
    public void Line_width_and_round_ends_are_the_variant(double scale, bool round, string variant)
    {
        var icon = W.Icon("ArrowUpRight").RoundLineEnds(round).LineWidthScale(scale);

        Assert.Equal(variant, icon.Variant);
        Assert.Equal("ArrowUpRight@" + variant, icon.Asset);
        Assert.Contains($"\"variant\":\"{variant}\"", Json(icon));
        Assert.Equal(("ArrowUpRight", scale, round), IconNode.ParseAsset(icon.Asset));
    }

    [Fact]
    public void Setting_them_back_removes_the_variant()
    {
        var icon = W.Icon("ArrowUpRight").LineWidthScale(3).RoundLineEnds().LineWidthScale(1).RoundLineEnds(false);

        Assert.Null(icon.Variant);
    }

    [Fact]
    public void A_name_with_an_at_sign_that_is_no_variant_is_the_whole_name()
    {
        Assert.Equal(("logo@2x", 1, false), IconNode.ParseAsset("logo@2x"));
    }

    [Fact]
    public void A_template_lists_its_icons_by_stored_name()
    {
        var layout = new LiveActivityLayout { LockScreen = W.Icon("ArrowUpRight").LineWidthScale(3).RoundLineEnds() };

        Assert.Equal(["ArrowUpRight@3r"], layout.Trees.SelectMany(WidgetTree.Icons));
    }

    static string Json(WidgetNode node) => new WidgetTimeline().Add(DateTimeOffset.UnixEpoch, node).ToJson();
}
