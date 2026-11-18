using Plugin.Maui.Spine.Controls;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>How long <see cref="BarChart"/> draws the parts of a stacked bar.</summary>
public class BarSegmentsTests
{
    [Fact]
    public void Parts_fill_the_bar_in_proportion()
    {
        var lengths = BarSegments.Lengths([18, 81, 1], 200, 0);

        Assert.Equal([36f, 162f, 2f], lengths.Select(l => MathF.Round(l, 3)));
    }

    [Fact]
    public void A_small_part_keeps_a_sliver_taken_from_the_others()
    {
        var lengths = BarSegments.Lengths([0.4, 80, 19.6], 100, 3);

        Assert.Equal(3, lengths[0], 3);
        Assert.Equal(100, lengths.Sum(), 3);
        Assert.True(lengths[1] < 80 && lengths[2] < 19.6f);
    }

    [Fact]
    public void A_part_of_zero_is_not_drawn()
    {
        var lengths = BarSegments.Lengths([0, 10, 0, 30, 0], 100, 3);

        Assert.Equal([0f, 25f, 0f, 75f, 0f], lengths);
    }

    [Fact]
    public void Nothing_to_show_draws_nothing()
    {
        Assert.All(BarSegments.Lengths([0, 0], 100, 3), l => Assert.Equal(0, l));
        Assert.Empty(BarSegments.Lengths([], 100, 3));
    }

    [Fact]
    public void Many_small_parts_share_a_short_bar()
    {
        var lengths = BarSegments.Lengths([1, 1, 1, 97], 8, 3);

        Assert.Equal(8, lengths.Sum(), 3);
        Assert.All(lengths, l => Assert.True(l > 0));
    }
}
