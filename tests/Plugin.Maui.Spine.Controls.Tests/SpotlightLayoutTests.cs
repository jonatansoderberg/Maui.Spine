using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>Where a spotlight's hole and tip go in a 400 × 800 window.</summary>
public class SpotlightLayoutTests
{
    const double Width = 400;
    const double Height = 800;

    [Fact]
    public void The_hole_is_the_target_grown_by_the_padding()
    {
        Assert.Equal((92d, 192d, 116d, 66d), SpotlightLayout.Hole((100, 200, 100, 50), 8, Width, Height));
    }

    [Fact]
    public void The_hole_stays_in_the_window()
    {
        Assert.Equal((0d, 0d, 58d, 40d), SpotlightLayout.Hole((-10, 2, 60, 30), 8, Width, Height));
        Assert.Equal((342d, 762d, 58d, 38d), SpotlightLayout.Hole((350, 770, 80, 60), 8, Width, Height));
    }

    [Fact]
    public void The_hole_has_the_targets_shape_grown_by_the_padding()
    {
        Assert.Equal(16, SpotlightLayout.HoleRadius(116, 60, 8, 8));
        Assert.Equal(8, SpotlightLayout.HoleRadius(116, 60, 0, 8));
        Assert.Equal(30, SpotlightLayout.HoleRadius(60, 60, double.MaxValue, 8));
        Assert.Equal(30, SpotlightLayout.HoleRadius(116, 60, 40, 8));
    }

    [Fact]
    public void The_tip_goes_below_the_hole_when_it_fits()
    {
        var (x, y, below) = SpotlightLayout.Tip((100, 100, 200, 50), 300, 120, Width, Height);

        Assert.True(below);
        Assert.Equal(50, x);
        Assert.Equal(100 + 50 + SpotlightLayout.Margin, y);
    }

    [Fact]
    public void The_tip_goes_above_a_hole_near_the_bottom()
    {
        var (_, y, below) = SpotlightLayout.Tip((300, 700, 80, 60), 300, 120, Width, Height, safeBottom: 34);

        Assert.False(below);
        Assert.Equal(700 - SpotlightLayout.Margin - 120, y);
    }

    [Fact]
    public void The_tip_is_kept_a_margin_inside_the_edges()
    {
        Assert.Equal(SpotlightLayout.Margin, SpotlightLayout.Tip((0, 100, 40, 40), 300, 120, Width, Height).X);
        Assert.Equal(Width - SpotlightLayout.Margin - 300, SpotlightLayout.Tip((360, 100, 40, 40), 300, 120, Width, Height).X);
    }

    [Fact]
    public void A_tall_hole_puts_the_tip_at_the_bottom_clear_of_the_home_indicator()
    {
        var (_, y, _) = SpotlightLayout.Tip((0, 80, 400, 680), 300, 120, Width, Height, safeTop: 50, safeBottom: 34);

        Assert.Equal(Height - 34 - SpotlightLayout.Margin - 120, y);
    }

    [Fact]
    public void The_hole_moves_from_one_place_to_the_next()
    {
        var from = (0d, 0d, 100d, 100d);
        var to = (100d, 200d, 200d, 50d);

        Assert.Equal(from, SpotlightLayout.Between(from, to, 0));
        Assert.Equal(to, SpotlightLayout.Between(from, to, 1));
        Assert.Equal((50d, 100d, 150d, 75d), SpotlightLayout.Between(from, to, 0.5));
        Assert.Equal(to, SpotlightLayout.Between(from, to, 3));
    }
}
