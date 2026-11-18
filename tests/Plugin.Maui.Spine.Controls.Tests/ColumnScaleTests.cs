using Plugin.Maui.Spine.Controls;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>How tall <see cref="ColumnChart"/> draws its columns.</summary>
public class ColumnScaleTests
{
    [Fact]
    public void The_top_is_the_highest_value()
    {
        Assert.Equal(8.8, ColumnScale.Top([7.7, 8.8, 8.2], null, null));
    }

    [Fact]
    public void A_reference_line_above_the_values_raises_the_top()
    {
        Assert.Equal(10, ColumnScale.Top([7.7, 8.8, 8.2], 10, null));
        Assert.Equal(8.8, ColumnScale.Top([7.7, 8.8, 8.2], 5, null));
    }

    [Fact]
    public void A_set_maximum_is_the_top()
    {
        Assert.Equal(5, ColumnScale.Top([1, 3, 0], 10, 5));
        Assert.Equal(0, ColumnScale.Top([], null, null));
    }

    [Fact]
    public void A_column_is_its_share_of_the_top_within_the_least_and_the_whole()
    {
        Assert.Equal(50, ColumnScale.Height(5, 10, 100, 4));
        Assert.Equal(4, ColumnScale.Height(0.1, 10, 100, 4));
        Assert.Equal(100, ColumnScale.Height(12, 10, 100, 4));
        Assert.Equal(0, ColumnScale.Height(0, 10, 100, 4));
        Assert.Equal(0, ColumnScale.Height(3, 0, 100, 4));
    }
}
