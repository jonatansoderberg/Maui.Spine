using System.ComponentModel;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>How often a highlight plays, how XAML text becomes a repeat, and when a paused run ends.</summary>
public class HighlightTimingTests
{
    [Theory]
    [InlineData("Once", 1, false)]
    [InlineData("once", 1, false)]
    [InlineData("3", 3, false)]
    [InlineData(" Forever ", -1, false)]
    [InlineData("UntilTapped", -1, true)]
    public void Repeat_reads_xaml_text(string text, int count, bool endsOnTap)
    {
        var repeat = (HighlightRepeat)TypeDescriptor.GetConverter(typeof(HighlightRepeat)).ConvertFromInvariantString(text)!;

        Assert.Equal(count, repeat.Count);
        Assert.Equal(endsOnTap, repeat.EndsOnTap);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("Twice")]
    [InlineData("")]
    public void Repeat_refuses_other_text(string text)
    {
        Assert.Throws<FormatException>(() => HighlightRepeat.Parse(text));
    }

    [Fact]
    public void Repeat_writes_what_it_reads()
    {
        foreach (var repeat in new[] { HighlightRepeat.Once, HighlightRepeat.Times(4), HighlightRepeat.Forever, HighlightRepeat.UntilTapped })
            Assert.Equal(repeat, HighlightRepeat.Parse(repeat.ToString()));
    }

    [Fact]
    public void Times_plays_at_least_once()
    {
        Assert.Equal(HighlightRepeat.Once, HighlightRepeat.Times(0));
        Assert.Equal(1, HighlightRepeat.Times(-5).Count);
    }

    [Theory]
    [InlineData(HighlightKind.Edge, -1)]
    [InlineData(HighlightKind.Trace, -1)]
    [InlineData(HighlightKind.Breathe, -1)]
    [InlineData(HighlightKind.Pulse, 1)]
    [InlineData(HighlightKind.Sheen, 1)]
    public void An_unset_repeat_is_the_kinds_own(HighlightKind kind, int count)
    {
        var resolved = HighlightCycles.Resolve(kind, default);

        Assert.Equal(count, resolved.Count);
        Assert.False(resolved.EndsOnTap);
    }

    [Fact]
    public void A_set_repeat_wins_over_the_kinds_own()
    {
        Assert.Equal(HighlightRepeat.Once, HighlightCycles.Resolve(HighlightKind.Edge, HighlightRepeat.Once));
        Assert.Equal(HighlightRepeat.UntilTapped, HighlightCycles.Resolve(HighlightKind.Pulse, HighlightRepeat.UntilTapped));
    }

    [Fact]
    public void A_run_lasts_its_cycles()
    {
        Assert.Equal(TimeSpan.FromSeconds(3 * 1.6), HighlightCycles.Length(HighlightKind.Pulse, HighlightRepeat.Times(3)));
        Assert.Equal(TimeSpan.FromSeconds(2.4), HighlightCycles.Length(HighlightKind.Edge, HighlightRepeat.Once));
        Assert.Null(HighlightCycles.Length(HighlightKind.Edge, default));
        Assert.Null(HighlightCycles.Length(HighlightKind.Pulse, HighlightRepeat.UntilTapped));
        Assert.Null(HighlightCycles.Length(HighlightKind.Sheen, HighlightRepeat.Forever));
    }

    [Fact]
    public void The_clock_counts_only_time_on_screen()
    {
        var clock = new HighlightClock(TimeSpan.FromSeconds(3));

        clock.Resume(At(10));
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Remaining(At(11)));

        // In the background from 11 s to 40 s: none of that counts.
        clock.Pause(At(11));
        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Remaining(At(40)));

        clock.Resume(At(40));
        Assert.Equal(TimeSpan.FromSeconds(0.5), clock.Remaining(At(41.5)));
        Assert.Equal(TimeSpan.Zero, clock.Remaining(At(50)));
    }

    [Fact]
    public void Resuming_twice_keeps_the_first_start()
    {
        var clock = new HighlightClock(TimeSpan.FromSeconds(3));

        clock.Resume(At(0));
        clock.Resume(At(2));

        Assert.Equal(TimeSpan.FromSeconds(2), clock.Shown(At(2)));
    }

    [Fact]
    public void A_run_without_an_end_has_no_time_left()
    {
        var clock = new HighlightClock(null);
        clock.Resume(At(0));

        Assert.Null(clock.Remaining(At(100)));
    }

    static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);
}
