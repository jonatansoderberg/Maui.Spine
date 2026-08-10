using System.Globalization;
using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.LiveResults;

/// <summary>One competition as LiveResults lists it, before it is matched to an Eventor event.</summary>
public sealed record LiveCompetition
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Organizer { get; init; }
    public required DateOnly Date { get; init; }
}

/// <summary>
/// LiveResults' JSON into the domain's live view.
/// </summary>
/// <remarks>
/// The API is loose where the domain is strict: the same field arrives as a number, as a
/// string, or as an empty string depending on the runner's state, so every read goes through
/// the tolerant helpers here rather than through the type system's optimism.
/// </remarks>
public sealed class LiveResultsNormalizer(TimeZoneInfo _zone)
{
    /// <summary>Times and start times are hundredths of a second.</summary>
    private const double TicksPerUnit = TimeSpan.TicksPerSecond / 100.0;

    public static LiveResultsNormalizer ForZone(string timeZoneId) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    public IReadOnlyList<LiveCompetition> Competitions(JsonElement payload)
    {
        if (!payload.TryGetProperty("competitions", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        var competitions = new List<LiveCompetition>(list.GetArrayLength());

        foreach (var element in list.EnumerateArray())
        {
            if (Integer(element, "id") is not { } id)
                continue;

            if (!DateOnly.TryParse(Text(element, "date"), CultureInfo.InvariantCulture, out var date))
                continue;

            competitions.Add(new LiveCompetition
            {
                Id = id,
                Name = Text(element, "name") ?? string.Empty,
                Organizer = Text(element, "organizer") ?? string.Empty,
                Date = date,
            });
        }

        return competitions;
    }

    public IReadOnlyList<string> Classes(JsonElement payload)
    {
        if (!payload.TryGetProperty("classes", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        return [.. list.EnumerateArray().Select(c => Text(c, "className")).OfType<string>()];
    }

    /// <summary>
    /// One class' rows. <paramref name="date"/> is the competition's date, because LiveResults
    /// reports a start time as a clock reading without saying which day it belongs to.
    /// </summary>
    public IReadOnlyList<LiveEntry> Entries(JsonElement payload, string className, DateOnly date)
    {
        if (!payload.TryGetProperty("results", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        var entries = new List<LiveEntry>(list.GetArrayLength());

        foreach (var element in list.EnumerateArray())
        {
            var name = Text(element, "name") ?? string.Empty;
            var club = Text(element, "club") ?? string.Empty;
            var splits = Splits(element);
            Passing? last = splits.Count > 0 ? splits[^1] : null;

            int rawStatus = Integer(element, "status") ?? 0;
            var status = StatusOf(rawStatus, splits.Count > 0);
            var time = Duration(element, "result");

            entries.Add(new LiveEntry
            {
                Person = new PersonId(RunnerIdentity.Of(name, club).Key),
                Name = name,
                Club = club,
                Class = className,
                StartTime = StartOf(element, date),
                Status = status,
                LastControlNumber = last?.Control,
                ElapsedAtLastControl = last?.Elapsed,
                // Position is the standing in the class either way: at the last radio while
                // running, at the finish once finished. The live list sorts on it, so leaving
                // it out for finished runners scatters them through the field.
                Position = status == LiveStatus.Finished ? Integer(element, "place") : last?.Place,
                FinishTime = status is LiveStatus.Finished ? time : null,
                FinalPlace = status == LiveStatus.Finished ? Integer(element, "place") : null,
            });
        }

        return entries;
    }

    /// <summary>
    /// LiveResults has one code for "has not finished", whether the runner is still in the
    /// forest or never started. A runner with a radio passing is out on the course.
    /// </summary>
    private static LiveStatus StatusOf(int status, bool hasPassings) => status switch
    {
        0 => LiveStatus.Finished,
        2 => LiveStatus.DidNotFinish,
        3 or 4 or 5 => LiveStatus.Mispunch,
        1 or 11 or 12 => LiveStatus.NotStarted,
        _ => hasPassings ? LiveStatus.Running : LiveStatus.NotStarted,
    };

    private DateTimeOffset StartOf(JsonElement element, DateOnly date)
    {
        var sinceMidnight = Duration(element, "start") ?? TimeSpan.Zero;
        var local = date.ToDateTime(TimeOnly.MinValue) + sinceMidnight;

        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Radio controls, in passing order. The keys are control codes with sibling keys for
    /// status, place and time behind — <c>1079</c>, <c>1079_place</c> and so on.
    /// </summary>
    private static IReadOnlyList<Passing> Splits(JsonElement element)
    {
        if (!element.TryGetProperty("splits", out var splits) || splits.ValueKind != JsonValueKind.Object)
            return [];

        var passings = new List<Passing>();

        foreach (var property in splits.EnumerateObject())
        {
            if (property.Name.Contains('_') || !int.TryParse(property.Name, out int control))
                continue;

            if (Duration(property.Value) is not { } elapsed)
                continue;

            passings.Add(new Passing(
                control,
                elapsed,
                Integer(splits, $"{property.Name}_place")));
        }

        return [.. passings.OrderBy(p => p.Elapsed)];
    }

    // ---------------------------------------------------------------- tolerant reads

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;

    /// <summary>A number that may arrive as a number, as a string, or as <c>"-"</c> for "none".</summary>
    private static int? Integer(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out int number) ? number : null,
            JsonValueKind.String => int.TryParse(value.GetString(), CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    private static TimeSpan? Duration(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? Duration(value) : null;

    private static TimeSpan? Duration(JsonElement value)
    {
        long? hundredths = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long number) ? number : null,
            JsonValueKind.String => long.TryParse(value.GetString(), CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null,
            _ => null,
        };

        return hundredths is > 0 ? TimeSpan.FromTicks((long)(hundredths.Value * TicksPerUnit)) : null;
    }

    private readonly record struct Passing(int Control, TimeSpan Elapsed, int? Place);
}
