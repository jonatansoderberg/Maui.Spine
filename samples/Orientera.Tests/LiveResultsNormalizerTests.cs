using System.Text.Json;
using Orientera.Backend.LiveResults;
using Orientera.Services.Sources;

namespace Orientera.Tests;

/// <summary>
/// LiveResults is a loose API — the same field arrives as a number, a string or an empty
/// string depending on how the runner's race went. These pin what the domain gets out of it.
/// </summary>
public class LiveResultsNormalizerTests
{
    private readonly LiveResultsNormalizer _normalizer = LiveResultsNormalizer.ForZone("Europe/Stockholm");
    private static readonly DateOnly RaceDay = new(2026, 8, 9);

    private IReadOnlyList<LiveEntry> D21() =>
        _normalizer.Entries(Fixture.LiveResults("classresults-d21.json"), "D21", RaceDay);

    private IReadOnlyList<LiveEntry> H21() =>
        _normalizer.Entries(Fixture.LiveResults("classresults-h21.json"), "H21", RaceDay);

    /// <summary>
    /// The payload is not valid JSON: competition names arrive with raw tabs inside the string
    /// values. Without repairing it there is no calendar at all.
    /// </summary>
    [Fact]
    public void A_payload_with_raw_control_characters_still_parses()
    {
        var raw = File.ReadAllText(Fixture.PathFor("LiveResults", "competitions.json"));

        Assert.Contains('\t', raw);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JsonElement>(raw));

        var repaired = JsonSerializer.Deserialize<JsonElement>(LiveResultsClient.Repair(raw));

        Assert.NotEqual(JsonValueKind.Undefined, repaired.ValueKind);
    }

    [Fact]
    public void The_calendar_becomes_competitions_with_dates()
    {
        var competitions = _normalizer.Competitions(Fixture.LiveResults("competitions.json"));

        var norrland = competitions.Single(c => c.Id == 37308);

        Assert.Equal("Norrlandsmästerskapen, medel", norrland.Name);
        Assert.Equal("Gävle OK", norrland.Organizer);
        Assert.Equal(RaceDay, norrland.Date);
    }

    [Fact]
    public void Classes_come_out_in_the_order_the_source_lists_them()
    {
        var classes = _normalizer.Classes(Fixture.LiveResults("classes.json"));

        Assert.Contains("D21", classes);
        Assert.Contains("H21", classes);
    }

    /// <summary>
    /// Times are hundredths of a second. Reading them as anything else is wrong by a factor of
    /// ten or a hundred, which is the kind of error a result list makes look plausible.
    /// </summary>
    [Fact]
    public void A_finish_time_is_read_in_hundredths()
    {
        var winner = D21().First(e => e.Name == "Jennie Börjesson Eriksson");

        Assert.Equal(LiveStatus.Finished, winner.Status);
        Assert.Equal(new TimeSpan(0, 35, 31), winner.FinishTime);
        Assert.Equal(1, winner.FinalPlace);
    }

    /// <summary>A start time is a clock reading; the day comes from the competition.</summary>
    [Fact]
    public void A_start_time_lands_on_the_race_day()
    {
        var winner = D21().First(e => e.Name == "Jennie Börjesson Eriksson");

        Assert.Equal(new DateTimeOffset(2026, 8, 9, 11, 50, 0, TimeSpan.FromHours(2)), winner.StartTime);
    }

    /// <summary>
    /// A runner who never started has an empty start time, and midnight is a real clock reading —
    /// reading one as the other put "Start 00:00" on the live list (#65).
    /// </summary>
    [Fact]
    public void A_runner_who_never_started_has_no_start_time()
    {
        var vit = _normalizer.Entries(Fixture.LiveResults("classresults-vit20.json"), "Vit 2,0", RaceDay);

        var absent = vit.First(e => e.Name == "Isabel Sjödin");

        Assert.Equal(LiveStatus.NotStarted, absent.Status);
        Assert.Null(absent.StartTime);

        // The same status with a start time is a different runner: one who has not started yet.
        var waiting = H21().First(e => e.Name == "Bo Roger Nordström");

        Assert.Equal(LiveStatus.NotStarted, waiting.Status);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 12, 6, 0, TimeSpan.FromHours(2)), waiting.StartTime);
    }

    [Fact]
    public void Radio_controls_become_the_runners_last_known_position()
    {
        var second = D21().First(e => e.Name == "Johanna Börjesson Eriksson");

        // A finished runner is placed by their finish, not by the last radio — the live list
        // sorts on Position, so it has to hold the standing in both states.
        Assert.Equal(LiveStatus.Finished, second.Status);
        Assert.Equal(2, second.Position);
        Assert.Equal(1088, second.LastPassing?.Control);
        Assert.Equal(new TimeSpan(0, 37, 27), second.LastPassing?.Elapsed);
    }

    /// <summary>The columns of the split table, and the order the course passes them.</summary>
    [Fact]
    public void The_class_carries_its_radio_controls_in_course_order()
    {
        var controls = _normalizer.Controls(Fixture.LiveResults("classresults-d21.json"));

        Assert.Equal([1079, 1088], controls.Select(c => c.Code));

        // The code the timing system uses is not the number written on the control.
        Assert.Equal(["79", "88"], controls.Select(c => c.Name));
    }

    /// <summary>
    /// Every passing, not just the last one: place and time behind at each radio is what makes
    /// a runner's progress through the field readable.
    /// </summary>
    [Fact]
    public void A_runner_carries_every_radio_passing_with_place_and_time_behind()
    {
        var third = D21().First(e => e.Name == "Emma Blixt");

        Assert.Equal([1079, 1088], third.Passings.Select(p => p.Control));
        Assert.Equal([5, 3], third.Passings.Select(p => p.Place));

        Assert.Equal(new TimeSpan(0, 25, 7), third.Passings[0].Elapsed);
        Assert.Equal(new TimeSpan(0, 3, 47), third.Passings[0].Behind);

        // Fifth at 79 and third at 88: she ran into the field, which is the whole point of
        // showing the controls beside each other.
        Assert.Equal(new TimeSpan(0, 4, 18), third.Passings[1].Behind);
    }

    [Fact]
    public void The_class_winner_is_the_one_with_no_time_behind()
    {
        var winner = D21().First(e => e.Name == "Jennie Börjesson Eriksson");

        Assert.All(winner.Passings, passing => Assert.Equal(1, passing.Place));
        Assert.All(winner.Passings, passing => Assert.Null(passing.Behind));
        Assert.Null(winner.FinishBehind);

        Assert.Equal(new TimeSpan(0, 4, 14), D21().First(e => e.Name == "Emma Blixt").FinishBehind);
    }

    /// <summary>A mispunch has times at the radios but no place at any of them.</summary>
    [Fact]
    public void A_mispunch_is_timed_but_not_placed()
    {
        var mispunch = H21().First(e => e.Name == "Pavel Balabanov");

        Assert.Equal([1079, 1088], mispunch.Passings.Select(p => p.Control));
        Assert.Equal(new TimeSpan(0, 30, 13), mispunch.Passings[0].Elapsed);
        Assert.All(mispunch.Passings, passing => Assert.Null(passing.Place));
    }

    [Theory]
    [InlineData("Bo Roger Nordström", LiveStatus.NotStarted)]
    [InlineData("Pavel Balabanov", LiveStatus.Mispunch)]
    public void The_sources_status_codes_become_the_domains(string name, LiveStatus expected) =>
        Assert.Equal(expected, H21().First(e => e.Name == name).Status);

    /// <summary>A runner who never started has no times, and the empty strings must not become zeroes.</summary>
    [Fact]
    public void A_runner_without_times_has_none()
    {
        var absent = H21().First(e => e.Name == "Bo Roger Nordström");

        Assert.Null(absent.FinishTime);
        Assert.Empty(absent.Passings);
        Assert.Null(absent.FinalPlace);
    }

    /// <summary>
    /// LiveResults has no person id, so the identity is the name and the club — which is what
    /// lets the app find itself and Min grupp in the list without telling anyone who they are.
    /// </summary>
    [Fact]
    public void A_runner_is_identified_by_name_and_club()
    {
        var winner = D21().First(e => e.Name == "Jennie Börjesson Eriksson");

        Assert.Equal(
            RunnerIdentity.Of("Jennie Börjesson Eriksson", "Malungs OK Skogsmårdarna").Key,
            winner.Person.Value);
    }
}
