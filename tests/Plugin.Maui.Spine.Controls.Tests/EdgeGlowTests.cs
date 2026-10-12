using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>The display corners the window's edge follows, and how its level eases.</summary>
public class EdgeGlowTests
{
    [Theory]
    [InlineData(402, 874, 62)]
    [InlineData(874, 402, 62)]
    [InlineData(393, 852, 55)]
    [InlineData(390, 844, 47.33)]
    [InlineData(375, 667, 0)]
    public void A_known_iPhone_has_its_radius_either_way_up(double width, double height, double radius)
    {
        Assert.Equal(radius, DisplayCorners.Radius(width, height, hasHomeIndicator: radius > 0, isPad: false));
    }

    [Fact]
    public void An_unknown_screen_is_rounded_only_with_a_home_indicator()
    {
        Assert.Equal(55, DisplayCorners.Radius(500, 1000, hasHomeIndicator: true, isPad: false));
        Assert.Equal(0, DisplayCorners.Radius(500, 1000, hasHomeIndicator: false, isPad: false));
    }

    [Fact]
    public void An_iPad_is_rounded_only_without_a_home_button()
    {
        Assert.Equal(18, DisplayCorners.Radius(820, 1180, hasHomeIndicator: true, isPad: true));
        Assert.Equal(0, DisplayCorners.Radius(768, 1024, hasHomeIndicator: false, isPad: true));
    }

    [Fact]
    public void The_level_eases_towards_the_target()
    {
        var level = new EdgeLevel();

        Assert.Equal(0.25, level.Step(1), 3);
        Assert.Equal(0.4375, level.Step(1), 3);
        for (var i = 0; i < 40; i++)
            level.Step(1);

        Assert.Equal(1, level.Value, 3);
    }

    [Fact]
    public void The_level_is_clamped_and_ignores_values_that_are_not_numbers()
    {
        var level = new EdgeLevel();

        level.Step(8);
        Assert.Equal(0.25, level.Value, 3);

        level.Step(double.NaN);
        Assert.Equal(0.1875, level.Value, 3);

        level.Step(-3);
        Assert.True(level.Value < 0.1875 && level.Value >= 0);
    }
}
