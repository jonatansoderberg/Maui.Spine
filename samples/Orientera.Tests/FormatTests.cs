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

    /// <summary>
    /// Eight of the twelve Swedish months abbreviate with a period of their own, so a sentence
    /// that adds its own ends in two — "faller ur 19 sep..". The four that do not are why the fix
    /// is not simply dropping the sentence's period (#115).
    /// </summary>
    [Fact]
    public void A_date_inside_a_sentence_leaves_the_full_stop_to_the_sentence()
    {
        Assert.Equal("19 sep", Format.DateInSentence(new DateOnly(2026, 9, 19)));
        Assert.Equal("19 maj", Format.DateInSentence(new DateOnly(2026, 5, 19)));

        Assert.Equal(
            "Ett räknande resultat faller ur 19 sep.",
            $"Ett räknande resultat faller ur {Format.DateInSentence(new DateOnly(2026, 9, 19))}.");
        Assert.Equal(
            "Ett räknande resultat faller ur 19 maj.",
            $"Ett räknande resultat faller ur {Format.DateInSentence(new DateOnly(2026, 5, 19))}.");
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

    /// <summary>
    /// The results list puts the placement in a column of its own, where the ordinal ending says
    /// the same thing on every row and only costs the number a fixed width.
    /// </summary>
    [Fact]
    public void A_placement_in_a_column_is_a_bare_number()
    {
        Assert.Equal("1", Format.PlaceNumber(1));
        Assert.Equal("109", Format.PlaceNumber(109));
        Assert.Equal("—", Format.PlaceNumber(null));
    }

    [Fact]
    public void Only_the_podium_gets_a_medal()
    {
        Assert.Equal("🥇", Format.Medal(1));
        Assert.Equal("🥈", Format.Medal(2));
        Assert.Equal("🥉", Format.Medal(3));
        Assert.Equal(string.Empty, Format.Medal(4));
        Assert.Equal(string.Empty, Format.Medal(null));
    }

    /// <summary>A field of nobody is a field the source did not state — say nothing, not "av 0".</summary>
    [Fact]
    public void The_field_is_named_only_when_its_size_is_known()
    {
        Assert.Equal("av 91", Format.OutOf(91));
        Assert.Equal(string.Empty, Format.OutOf(0));
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

    /// <summary>
    /// A weekday alone cannot be acted on — "torsdag" is either three days away or ten, and the
    /// difference is the whole point of a deadline.
    /// </summary>
    [Fact]
    public void A_deadline_says_the_day_the_date_and_the_countdown()
    {
        var today = new DateOnly(2026, 8, 17);

        Assert.Equal("torsdag 20 aug (om 3 dagar)", Format.Deadline(new DateOnly(2026, 8, 20), today));
    }

    [Fact]
    public void A_deadline_today_or_tomorrow_says_so()
    {
        var today = new DateOnly(2026, 8, 17);

        Assert.EndsWith("(idag)", Format.Deadline(today, today));
        Assert.EndsWith("(imorgon)", Format.Deadline(today.AddDays(1), today));
        Assert.EndsWith("(har stängt)", Format.Deadline(today.AddDays(-1), today));
    }

    /// <summary>
    /// The date is written as a person writes it: "torsdag 20 augusti", not "20:e". The ordinal
    /// form belongs to speech and to a day named on its own, and the test run read "20:e aug" as
    /// a bug in the formatting rather than as a date.
    /// </summary>
    [Theory]
    [InlineData(1, "1 mars")]
    [InlineData(2, "2 mars")]
    [InlineData(3, "3 mars")]
    [InlineData(11, "11 mars")]
    [InlineData(21, "21 mars")]
    [InlineData(31, "31 mars")]
    public void The_day_is_a_plain_number(int day, string expected)
    {
        var date = new DateOnly(2026, 3, day);

        Assert.Contains(expected, Format.Deadline(date, date.AddDays(-1)));
    }

    /// <summary>
    /// The column says it does not know, in the same em dash every other column in the app uses.
    /// The alternative was 6905 km — the distance to the Gulf of Guinea, where a competition with
    /// no published arena appears to be.
    /// </summary>
    [Fact]
    public void An_unknown_distance_is_an_em_dash()
    {
        Assert.Equal("—", Format.Distance((double?)null));
        Assert.Equal("41 km", Format.Distance((double?)41));
    }
}
