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

    [Fact]
    public void Radio_controls_become_the_runners_last_known_position()
    {
        var second = D21().First(e => e.Name == "Johanna Börjesson Eriksson");

        // A finished runner is placed by their finish, not by the last radio — the live list
        // sorts on Position, so it has to hold the standing in both states.
        Assert.Equal(LiveStatus.Finished, second.Status);
        Assert.Equal(2, second.Position);
        Assert.Equal(1088, second.LastControlNumber);
        Assert.Equal(new TimeSpan(0, 37, 27), second.ElapsedAtLastControl);
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
        Assert.Null(absent.ElapsedAtLastControl);
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
