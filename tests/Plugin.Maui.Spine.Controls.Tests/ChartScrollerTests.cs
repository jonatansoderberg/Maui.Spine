using Plugin.Maui.Spine.Controls;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>What <see cref="ChartScroller"/> works out before it moves or loads: its days, its motion and the names over the plot.</summary>
public class ChartScrollerTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private static DateTimeOffset Local(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(new DateTime(year, month, day, hour, minute, 0), Stockholm.GetUtcOffset(new DateTime(year, month, day, hour, minute, 0)));

    // Days

    [Fact]
    public void A_day_begins_at_local_midnight()
    {
        var day = ChartChunks.Day(Local(2026, 11, 18, 13), Stockholm);
        Assert.Equal(Local(2026, 11, 18), ChartChunks.StartOf(day, Stockholm));
        Assert.Equal(day, ChartChunks.Day(Local(2026, 11, 18, 23, 59), Stockholm));
        Assert.Equal(day + 1, ChartChunks.Day(Local(2026, 11, 19), Stockholm));
    }

    [Fact]
    public void The_day_the_clock_goes_back_is_25_hours()
    {
        var (from, to) = ChartChunks.Bounds(ChartChunks.Of(Local(2026, 10, 25, 12), Stockholm, 1), Stockholm, 1);
        Assert.Equal(TimeSpan.FromHours(25), to - from);
    }

    [Fact]
    public void The_day_the_clock_goes_forward_is_23_hours()
    {
        var (from, to) = ChartChunks.Bounds(ChartChunks.Of(Local(2026, 3, 29, 12), Stockholm, 1), Stockholm, 1);
        Assert.Equal(TimeSpan.FromHours(23), to - from);
    }

    [Fact]
    public void A_window_ending_at_midnight_does_not_reach_the_next_day()
    {
        var (first, last) = ChartChunks.Range(Local(2026, 11, 17), Local(2026, 11, 18), Stockholm, 1);
        Assert.Equal(first, last);
    }

    [Fact]
    public void Chunks_of_a_week_are_the_same_days_whatever_the_window()
    {
        var a = ChartChunks.Of(Local(2026, 11, 18), Stockholm, 7);
        var (from, to) = ChartChunks.Bounds(a, Stockholm, 7);
        Assert.True(from <= Local(2026, 11, 18) && Local(2026, 11, 18) < to);
        Assert.Equal(TimeSpan.FromDays(7), to - from);
        Assert.Equal(a, ChartChunks.Of(from, Stockholm, 7));
        Assert.Equal(a - 1, ChartChunks.Of(from.AddTicks(-1), Stockholm, 7));
    }

    // Motion

    [Fact]
    public void A_flick_carries_as_a_scroll_view_decelerates()
    {
        Assert.Equal(499, ScrollMotion.Carry(1), 3);
        Assert.Equal(-998, ScrollMotion.Carry(-2), 3);
    }

    [Fact]
    public void The_speed_is_measured_over_the_last_moments_of_the_drag()
    {
        var motion = new ScrollMotion();
        motion.Add(0, 0);
        motion.Add(200, 10);
        motion.Add(260, 40);
        motion.Add(300, 60);
        Assert.Equal(0.5, motion.Velocity(300), 3);
    }

    [Fact]
    public void A_drag_that_rested_before_letting_go_does_not_fling()
    {
        var motion = new ScrollMotion();
        motion.Add(0, 0);
        motion.Add(50, 40);
        Assert.Equal(0, motion.Velocity(400));
    }

    [Fact]
    public void The_rubber_band_gives_less_and_less_and_never_a_whole_width()
    {
        var a = ScrollMotion.RubberBand(50, 300);
        var b = ScrollMotion.RubberBand(100, 300);
        Assert.True(a < 50 && b < 100 && b - a < a);
        Assert.True(ScrollMotion.RubberBand(100_000, 300) < 300);
        Assert.Equal(-a, ScrollMotion.RubberBand(-50, 300), 6);
    }

    [Fact]
    public void A_day_rests_on_the_nearer_local_midnight()
    {
        Assert.Equal(Local(2026, 11, 18), ScrollMotion.Snap(Local(2026, 11, 18, 11), TimeSpan.FromDays(1), Stockholm));
        Assert.Equal(Local(2026, 11, 19), ScrollMotion.Snap(Local(2026, 11, 18, 13), TimeSpan.FromDays(1), Stockholm));
    }

    [Fact]
    public void An_hour_rests_on_a_round_hour_after_the_clock_went_back()
    {
        // 25 October 2026 in Sweden: 02:00 comes twice; 05:20 on the wall clock rests on 05:00, not 04:00 or 06:00.
        var snapped = ScrollMotion.Snap(Local(2026, 10, 25, 5, 20), TimeSpan.FromHours(1), Stockholm);
        Assert.Equal(Local(2026, 10, 25, 5), snapped);
    }

    // Names over the plot

    [Fact]
    public void A_name_stands_inside_where_its_day_begins()
    {
        var placed = SectionLayout.Place([new(-200, 60, 30), new(100, 60, 30)], 300, 8, 4);
        Assert.Equal([0f, 104f], placed.Select(p => p.X));
        Assert.All(placed, p => Assert.False(p.Short));
    }

    [Fact]
    public void The_leading_name_is_shortened_then_pushed_out_fading()
    {
        // Room for the short name only.
        var shortened = SectionLayout.Place([new(-200, 60, 30), new(36, 60, 30)], 300, 8, 4)[0];
        Assert.True(shortened.Short);
        Assert.Equal(0, shortened.X);
        Assert.Equal(1, shortened.Opacity);

        // No room even for that: pushed left, half gone.
        var pushed = SectionLayout.Place([new(-200, 60, 30), new(19, 60, 30)], 300, 8, 4)[0];
        Assert.True(pushed.Short);
        Assert.Equal(-15, pushed.X);
        Assert.Equal(0.5, pushed.Opacity, 3);
    }

    [Fact]
    public void A_name_reaching_in_at_the_trailing_edge_fades_in_and_none_past_it_is_drawn()
    {
        var placed = SectionLayout.Place([new(-200, 60, 30), new(266, 60, 30), new(400, 60, 30)], 300, 8, 4);
        Assert.Equal(2, placed.Count);
        Assert.Equal(0.5, placed[1].Opacity, 3);
    }

    [Fact]
    public void Two_names_never_overlap()
    {
        for (var offset = -100f; offset < 300; offset += 3)
        {
            var placed = SectionLayout.Place([new(offset - 300, 70, 32), new(offset, 70, 32), new(offset + 300, 70, 32)], 300, 8, 4);
            for (var i = 1; i < placed.Count; i++)
            {
                var before = placed[i - 1];
                var width = before.Short ? 32 : 70;
                Assert.True(before.X + width <= placed[i].X - 8 + 0.01f, $"at {offset}");
            }
        }
    }
}
