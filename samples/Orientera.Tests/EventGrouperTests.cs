using Orientera.Services.FakeData;
using Orientera.Services.Grouping;

namespace Orientera.Tests;

public class EventGrouperTests
{
    private static Competition Make(
        string name,
        DateOnly date,
        string organiser = "Gävle OK",
        string place = "Hemlingby, Gävle",
        Discipline discipline = Discipline.Middle,
        CompetitionLevel level = CompetitionLevel.Recreational) => new()
    {
        Id = new CompetitionId($"{name}-{date:yyyyMMdd}"),
        Name = name,
        Organiser = organiser,
        District = "Gästrikland",
        Place = place,
        Location = new GeoPoint(60.6489, 17.1339),
        Discipline = discipline,
        Level = level,
        FirstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(date.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(2)),
    };

    [Fact]
    public void The_spec_example_collapses_to_one_card()
    {
        // "Sex Eventor-rader 4–9 augusti ska normalt visas som ett kort."
        var rows = Enumerable.Range(0, 6)
            .Select(i => Make($"Veckans bana etapp {i + 1}", new DateOnly(2026, 8, 4).AddDays(i)))
            .ToList();

        var groups = EventGrouper.Group(rows);

        var group = Assert.Single(groups);
        Assert.Equal("Veckans bana", group.Title);
        Assert.Equal("Hemlingby, Gävle", group.Place);
        Assert.Equal(6, group.Occurrences.Count);
        Assert.True(group.IsRecurring);
        Assert.Equal(new DateOnly(2026, 8, 4), group.FirstDate);
        Assert.Equal(new DateOnly(2026, 8, 9), group.LastDate);
    }

    [Fact]
    public void The_originals_stay_reachable_inside_the_group()
    {
        var rows = Enumerable.Range(0, 6)
            .Select(i => Make($"Veckans bana etapp {i + 1}", new DateOnly(2026, 8, 4).AddDays(i)))
            .ToList();

        var group = Assert.Single(EventGrouper.Group(rows));

        Assert.Equal(rows.Select(r => r.Id), group.Occurrences.Select(o => o.Id));
    }

    [Fact]
    public void Two_weeks_with_a_closed_day_between_them_stay_separate()
    {
        var week1 = Enumerable.Range(0, 6).Select(i => Make($"Veckans bana etapp {i + 1}", new DateOnly(2026, 8, 4).AddDays(i)));
        var week2 = Enumerable.Range(0, 6).Select(i => Make($"Veckans bana etapp {i + 7}", new DateOnly(2026, 8, 11).AddDays(i)));

        var groups = EventGrouper.Group([.. week1, .. week2]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(6, g.Occurrences.Count));
        Assert.Equal(new DateOnly(2026, 8, 4), groups[0].FirstDate);
        Assert.Equal(new DateOnly(2026, 8, 11), groups[1].FirstDate);
    }

    [Fact]
    public void A_single_competition_becomes_a_group_of_one()
    {
        var groups = EventGrouper.Group([Make("Hemlingbyloppet", new DateOnly(2026, 8, 2), level: CompetitionLevel.District)]);

        var group = Assert.Single(groups);
        Assert.False(group.IsRecurring);
        Assert.Equal("Hemlingbyloppet", group.Title);
    }

    [Fact]
    public void A_championship_weekend_is_not_merged_because_the_disciplines_differ()
    {
        var lng = Make("Norrlandsmästerskapen Lång", new DateOnly(2026, 8, 15),
            organiser: "Sandvikens OK", place: "Näset, Sandviken",
            discipline: Discipline.Long, level: CompetitionLevel.Championship);

        var middle = Make("Norrlandsmästerskapen Medel", new DateOnly(2026, 8, 16),
            organiser: "Sandvikens OK", place: "Näset, Sandviken",
            discipline: Discipline.Middle, level: CompetitionLevel.Championship);

        Assert.Equal(2, EventGrouper.Group([lng, middle]).Count);
    }

    [Fact]
    public void Same_title_but_a_different_organiser_is_a_different_series()
    {
        var a = Make("Veckans bana etapp 1", new DateOnly(2026, 8, 4));
        var b = Make("Veckans bana etapp 2", new DateOnly(2026, 8, 5), organiser: "OK Gästrike");

        Assert.Equal(2, EventGrouper.Group([a, b]).Count);
    }

    [Fact]
    public void Same_title_but_a_different_place_is_a_different_series()
    {
        var a = Make("Veckans bana etapp 1", new DateOnly(2026, 8, 4));
        var b = Make("Veckans bana etapp 2", new DateOnly(2026, 8, 5), place: "Valbo");

        Assert.Equal(2, EventGrouper.Group([a, b]).Count);
    }

    [Theory]
    [InlineData("Veckans bana etapp 3", "veckans bana")]
    [InlineData("Veckans bana #4", "veckans bana")]
    [InlineData("Veckans bana, deltävling 2", "veckans bana")]
    [InlineData("Gästriklandsserien deltävling 5", "gastriklandsserien")]
    [InlineData("Natt-KM", "natt km")]
    public void Titles_normalise_past_diacritics_punctuation_and_occurrence_numbering(string input, string expected)
    {
        Assert.Equal(expected, EventGrouper.NormalizeTitle(input));
    }

    [Fact]
    public void Groups_come_back_in_date_order()
    {
        var competitions = new[]
        {
            Make("Höstträffen", new DateOnly(2026, 9, 5), level: CompetitionLevel.National),
            Make("Hemlingbyloppet", new DateOnly(2026, 8, 2), level: CompetitionLevel.District),
            Make("DM Sprint", new DateOnly(2026, 8, 29), discipline: Discipline.Sprint, level: CompetitionLevel.Championship),
        };

        var groups = EventGrouper.Group(competitions);

        Assert.Equal(groups.OrderBy(g => g.FirstDate).Select(g => g.Title), groups.Select(g => g.Title));
    }

    [Fact]
    public void The_seeded_calendar_produces_exactly_two_veckans_bana_cards()
    {
        var groups = EventGrouper.Group(FakeDataset.Instance.Competitions);

        var recurring = groups.Where(g => g.IsRecurring).ToList();

        Assert.Equal(2, recurring.Count);
        Assert.All(recurring, g => Assert.Equal("Veckans bana", g.Title));
        Assert.All(recurring, g => Assert.Equal(6, g.Occurrences.Count));
    }
}
