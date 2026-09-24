using Plugin.Maui.Spine.Svg;
using Xunit;

namespace Plugin.Maui.Spine.Svg.Tests;

/// <summary>Which embedded resource a short SVG name resolves to (#371).</summary>
public class SvgResourceMatchTests
{
    private static readonly string[] Icons =
    [
        "Plugin.Maui.Spine.Svg.Icons.Images.Clock.svg",
        "Plugin.Maui.Spine.Svg.Icons.Images.SmartClock.svg",
        "Plugin.Maui.Spine.Svg.Icons.Images.Unlock.svg",
        "Plugin.Maui.Spine.Svg.Icons.Images.Lock.svg",
    ];

    [Theory]
    [InlineData("lock.svg", "Plugin.Maui.Spine.Svg.Icons.Images.Lock.svg")]
    [InlineData("clock.svg", "Plugin.Maui.Spine.Svg.Icons.Images.Clock.svg")]
    [InlineData("UNLOCK.SVG", "Plugin.Maui.Spine.Svg.Icons.Images.Unlock.svg")]
    public void The_whole_file_name_must_match(string fileName, string expected)
    {
        Assert.Equal(expected, SvgResourceMatch.Find(Icons, fileName, out var ambiguous));
        Assert.False(ambiguous);
    }

    [Fact]
    public void A_name_that_is_only_a_suffix_finds_nothing()
    {
        Assert.Null(SvgResourceMatch.Find(["App.Images.Clock.svg"], "lock.svg", out _));
    }

    [Fact]
    public void A_folder_path_in_the_name_still_matches()
    {
        Assert.Equal("App.Images.Dark.icon.svg", SvgResourceMatch.Find(["App.Images.Dark.icon.svg", "App.Images.icon.svg"], "Dark.icon.svg", out _));
    }

    [Fact]
    public void The_same_file_twice_is_reported_and_resolves_to_the_shorter_name()
    {
        var match = SvgResourceMatch.Find(["App.Resources.Images.icon.svg", "Lib.icon.svg"], "icon.svg", out var ambiguous);

        Assert.True(ambiguous);
        Assert.Equal("Lib.icon.svg", match);
    }
}
