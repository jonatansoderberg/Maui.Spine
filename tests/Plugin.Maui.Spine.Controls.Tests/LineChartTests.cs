using Plugin.Maui.Spine.Controls;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>What <see cref="LineChart"/> does before it draws: its clock, its series and its smoothing.</summary>
public class LineChartTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static ChartPoint[] EveryMinute(int count, Func<int, double> value) =>
        [.. Enumerable.Range(0, count).Select(i => new ChartPoint(Start.AddMinutes(i), value(i)))];

    // The clock

    [Fact]
    public void Labels_read_the_wall_clock_after_winter_time()
    {
        // 24 October 23:00 to 25 October 07:00 in Sweden: the clock went from 03:00 back to 02:00.
        var from = new DateTimeOffset(2026, 10, 24, 21, 0, 0, TimeSpan.Zero);
        var ticks = LocalClock.Ticks(from, from.AddHours(9), TimeSpan.FromHours(1), Stockholm).Select(t => t.Wall.ToString("HH:mm")).ToList();
        Assert.Equal(["00:00", "01:00", "02:00", "02:00", "03:00", "04:00", "05:00", "06:00", "07:00"], ticks);
    }

    [Fact]
    public void The_hour_skipped_in_spring_has_no_label()
    {
        var from = new DateTimeOffset(2026, 3, 28, 22, 0, 0, TimeSpan.Zero);
        var ticks = LocalClock.Ticks(from, from.AddHours(6), TimeSpan.FromHours(1), Stockholm).Select(t => t.Wall.ToString("HH:mm")).ToList();
        Assert.Equal(["00:00", "01:00", "03:00", "04:00", "05:00", "06:00"], ticks);
    }

    [Fact]
    public void Finds_where_the_clock_changed_and_by_how_much()
    {
        var autumn = LocalClock.Changes(new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), Stockholm).Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), autumn.At);
        Assert.Equal(TimeSpan.FromHours(-1), autumn.Change);

        var spring = LocalClock.Changes(new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero), new(2026, 3, 29, 12, 0, 0, TimeSpan.Zero), Stockholm).Single();
        Assert.Equal(TimeSpan.FromHours(1), spring.Change);

        Assert.Empty(LocalClock.Changes(new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), Stockholm));
    }

    [Fact]
    public void A_day_in_another_zone_has_its_own_midnight()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var from = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

        var ticks = LocalClock.Ticks(from.AddMinutes(-1), from.AddDays(1), TimeSpan.FromHours(6), tokyo).Select(t => t.Wall.ToString("HH")).ToList();

        Assert.Equal(["00", "06", "12", "18", "00"], ticks);
    }

    // Gaps

    [Fact]
    public void A_gap_is_three_times_the_usual_spacing()
    {
        var times = EveryMinute(30, _ => 5).Select(p => p.Time).ToList();

        Assert.Equal(TimeSpan.FromMinutes(3), LineSeries.GapThreshold(times, null, null));
        Assert.Equal(TimeSpan.FromMinutes(7), LineSeries.GapThreshold(times, null, TimeSpan.FromMinutes(7)));
        Assert.Equal(TimeSpan.FromMinutes(20), LineSeries.GapThreshold(times, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(7)));
    }

    [Fact]
    public void Points_split_where_they_pause()
    {
        ChartPoint[] points = [.. EveryMinute(10, _ => 5), .. EveryMinute(10, _ => 6).Select(p => p with { Time = p.Time.AddMinutes(30) })];

        var runs = LineSeries.Split(points, p => p.Time, TimeSpan.FromMinutes(3));

        Assert.Equal([10, 10], runs.Select(r => r.Length));
    }

    // Thinning

    [Fact]
    public void Thinning_merges_close_points_and_keeps_the_last_as_it_is()
    {
        var points = EveryMinute(60, i => i);

        var thinned = LineSeries.Thin(points, TimeSpan.FromMinutes(5).TotalSeconds, TimeSpan.FromMinutes(3), null, null);

        Assert.InRange(thinned.Length, 12, 14);
        Assert.Equal(points[^1], thinned[^1]);
        Assert.Equal(2, thinned[0].Value);
    }

    [Fact]
    public void Thinning_never_hides_a_value_outside_the_band()
    {
        var points = EveryMinute(60, i => i switch { 17 => 2.8, 41 => 14.5, _ => 6 });

        var thinned = LineSeries.Thin(points, TimeSpan.FromMinutes(10).TotalSeconds, TimeSpan.FromMinutes(3), 3.9, 10);

        Assert.Contains(points[17], thinned);
        Assert.Contains(points[41], thinned);
    }

    [Fact]
    public void Nothing_is_merged_across_a_gap()
    {
        ChartPoint[] points = [.. EveryMinute(5, _ => 5), .. EveryMinute(5, _ => 9).Select(p => p with { Time = p.Time.AddMinutes(8) })];

        var thinned = LineSeries.Thin(points, TimeSpan.FromMinutes(30).TotalSeconds, TimeSpan.FromMinutes(3), null, null);

        Assert.Equal([5, 9, 9], thinned.Select(p => p.Value));
    }

    // The value axis

    [Fact]
    public void The_axis_ends_on_round_steps_with_axes()
    {
        var (min, max, step) = LineSeries.ValueAxis([3.9, 10, 12.3, 4.4], null, null, round: true);

        Assert.Equal(5, step);
        Assert.Equal(0, min);
        Assert.Equal(15, max);
    }

    [Fact]
    public void A_small_graph_gets_about_a_tenth_of_room_each_way()
    {
        var (min, max, _) = LineSeries.ValueAxis([5, 15], null, null, round: false);

        // The top's room is a tenth of the range after the bottom's: as Glukos's graphs have always been drawn.
        Assert.Equal(4, min, 6);
        Assert.Equal(16.1, max, 6);
    }

    [Fact]
    public void Set_ends_are_kept()
    {
        var (min, max, _) = LineSeries.ValueAxis([5, 15], 0, 21, round: true);

        Assert.Equal((0d, 21d), (min, max));
    }

    [Theory]
    [InlineData(0.7, 1)]
    [InlineData(1.4, 2)]
    [InlineData(3.3, 5)]
    [InlineData(7, 10)]
    [InlineData(23, 50)]
    public void Steps_are_1_2_or_5_times_a_power_of_ten(double rough, double step) =>
        Assert.Equal(step, LineSeries.NiceStep(rough), 9);

    // Smoothing

    [Fact]
    public void Smoothing_keeps_the_last_point_and_points_asked_for()
    {
        var points = Enumerable.Range(0, 20).Select(i => ((float)i * 10, i % 2 == 0 ? 100f : 110f)).ToArray();

        var eased = ChartSmoothing.Gaussian(points, 15, i => i == 7);

        Assert.Equal(points[^1], eased[^1]);
        Assert.Equal(points[7], eased[7]);
        Assert.InRange(eased[10].Item2, 102, 108);
    }

    [Fact]
    public void Smoothing_a_straight_line_leaves_it_straight()
    {
        var points = Enumerable.Range(0, 20).Select(i => ((float)i * 10, 50f + i * 3)).ToArray();

        var eased = ChartSmoothing.Gaussian(points, 25);

        Assert.All(eased.Zip(points), p => Assert.Equal(p.Second.Item2, p.First.Item2, 2));
    }

    [Fact]
    public void A_curve_goes_through_every_point()
    {
        (float X, float Y)[] points = [(0, 0), (10, 5), (20, 2), (30, 8)];

        var segments = ChartSmoothing.CatmullRom(points);

        Assert.Equal(points[1..], segments.Select(s => s.End));
    }
}
