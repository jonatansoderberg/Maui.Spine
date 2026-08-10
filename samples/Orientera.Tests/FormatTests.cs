using Orientera.Presentation;

namespace Orientera.Tests;

/// <summary>
/// The spoken forms exist because a screen reader reads "38:33" as a clock time. They are the
/// only text a VoiceOver user gets for a result, so they are worth pinning down.
/// </summary>
public class FormatTests
{
    [Theory]
    [InlineData(0, 38, 33, "38 minuter 33 sekunder")]
    [InlineData(0, 1, 0, "1 minut")]
    [InlineData(0, 0, 1, "1 sekund")]
    [InlineData(0, 0, 0, "0 sekunder")]
    [InlineData(1, 0, 15, "1 timme 15 sekunder")]
    [InlineData(2, 4, 59, "2 timmar 4 minuter 59 sekunder")]
    public void Spoken_times_are_grammatical(int hours, int minutes, int seconds, string expected)
    {
        var time = new TimeSpan(hours, minutes, seconds);

        Assert.Equal(expected, Format.SpokenTime(time));
    }

    [Fact]
    public void Spoken_deltas_say_which_side_of_the_comparison_they_are_on()
    {
        Assert.Equal("1 minut 7 sekunder efter", Format.SpokenDelta(TimeSpan.FromSeconds(67)));
        Assert.Equal("14 sekunder före", Format.SpokenDelta(TimeSpan.FromSeconds(-14)));
        Assert.Equal("samma tid", Format.SpokenDelta(TimeSpan.Zero));
        Assert.Equal(string.Empty, Format.SpokenDelta(null));
    }

    [Fact]
    public void Placements_are_written_as_ordinals_and_spoken_as_words()
    {
        Assert.Equal("1:a", Format.Place(1));
        Assert.Equal("2:a", Format.Place(2));
        Assert.Equal("3:e", Format.Place(3));
        Assert.Equal("plats 3", Format.SpokenPlace(3));
        Assert.Equal("ingen placering", Format.SpokenPlace(null));
    }

    [Theory]
    [InlineData(0, 31, 12, "31:12")]
    [InlineData(1, 4, 59, "1:04:59")]
    [InlineData(0, 0, 7, "0:07")]
    public void Written_times_drop_the_hour_when_there_is_none(int h, int m, int s, string expected)
    {
        Assert.Equal(expected, Format.Time(new TimeSpan(h, m, s)));
    }

    [Fact]
    public void Deltas_use_a_real_minus_sign_not_a_hyphen()
    {
        Assert.StartsWith("−", Format.Delta(TimeSpan.FromSeconds(-14)));
        Assert.StartsWith("+", Format.Delta(TimeSpan.FromSeconds(14)));
    }

    [Fact]
    public void Date_ranges_collapse_a_shared_month()
    {
        Assert.Equal("4–9 aug.", Format.DateRange(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 9)));
    }

    [Theory]
    [InlineData(0, "idag")]
    [InlineData(1, "imorgon")]
    [InlineData(-1, "igår")]
    public void Near_dates_are_named_rather_than_numbered(int offset, string expected)
    {
        var today = new DateOnly(2026, 8, 15);

        Assert.Equal(expected, Format.RelativeDate(today.AddDays(offset), today));
    }
}
