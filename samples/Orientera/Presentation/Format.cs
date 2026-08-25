using System.Globalization;
using Orientera.Domain;

namespace Orientera.Presentation;

/// <summary>
/// Swedish formatting for the values that recur across every tab. ViewModels hand finished
/// strings to XAML rather than routing through converters — one place to change how a time or
/// a placement reads, and no converter plumbing in the bindings.
/// </summary>
public static class Format
{
    private static readonly CultureInfo Sv = new("sv-SE");

    /// <summary>The app's language, for callers that format their own dates.</summary>
    public static CultureInfo Culture => Sv;

    /// <summary>"48:07" under an hour, "1:04:59" above it.</summary>
    public static string Time(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    public static string Time(TimeSpan? time) => time is { } t ? Time(t) : "—";

    /// <summary>
    /// A time as a screen reader should say it. "38:33" is read as a clock time or as two
    /// separate numbers depending on the platform; "38 minuter 33 sekunder" is unambiguous.
    /// </summary>
    public static string SpokenTime(TimeSpan time)
    {
        var parts = new List<string>(3);

        int hours = (int)time.TotalHours;

        if (hours > 0)
            parts.Add($"{hours} {(hours == 1 ? "timme" : "timmar")}");

        if (time.Minutes > 0)
            parts.Add($"{time.Minutes} {(time.Minutes == 1 ? "minut" : "minuter")}");

        if (time.Seconds > 0 || parts.Count == 0)
            parts.Add($"{time.Seconds} {(time.Seconds == 1 ? "sekund" : "sekunder")}");

        return string.Join(' ', parts);
    }

    public static string SpokenTime(TimeSpan? time) => time is { } t ? SpokenTime(t) : "ingen tid";

    /// <summary>A signed difference in words: "1 minut 7 sekunder efter".</summary>
    public static string SpokenDelta(TimeSpan? delta) => delta switch
    {
        null => string.Empty,
        { Ticks: 0 } => "samma tid",
        { Ticks: < 0 } d => $"{SpokenTime(d.Duration())} före",
        { } d => $"{SpokenTime(d)} efter",
    };

    /// <summary>"3:e" is read as "3 e" — say "tredje plats" instead.</summary>
    public static string SpokenPlace(int? place) => place is { } p ? $"plats {p}" : "ingen placering";

    /// <summary>Signed difference: "+1:07" behind, "−0:14" ahead. Uses a real minus sign.</summary>
    public static string Delta(TimeSpan delta)
    {
        string sign = delta < TimeSpan.Zero ? "−" : "+";
        return sign + Time(delta.Duration());
    }

    public static string Delta(TimeSpan? delta) => delta is { } d ? Delta(d) : string.Empty;

    /// <summary>Swedish ordinal placement: "1:a", "2:a", "3:e".</summary>
    public static string Place(int place) =>
        place is 1 or 2 ? $"{place}:a" : $"{place}:e";

    public static string Place(int? place) => place is { } p ? Place(p) : "—";

    /// <summary>The placement as a bare number — "33" — or an em dash where there is none.</summary>
    /// <remarks>
    /// For a column that carries nothing but placements. The ordinal ending is what a sentence
    /// needs and a column does not: it says the same thing on every row, and it makes "1:a" and
    /// "109:e" two different widths for one kind of fact.
    /// </remarks>
    public static string PlaceNumber(int? place) => place is { } p ? p.ToString(Sv) : "—";

    /// <summary>Gold, silver or bronze for a podium place; nothing for the rest of the field.</summary>
    public static string Medal(int? place) => place switch
    {
        1 => "🥇",
        2 => "🥈",
        3 => "🥉",
        _ => string.Empty,
    };

    /// <summary>"av 91" — the field a placement is out of, or nothing when its size is unknown.</summary>
    public static string OutOf(int starters) => starters > 0 ? $"av {starters}" : string.Empty;

    /// <summary>
    /// Which half of a club's list a place is in. Said out loud because a club place is counted
    /// per section — 17th in a club means 17th among its men, not 17th of everyone.
    /// </summary>
    public static string Section(RankingSection section) => section switch
    {
        RankingSection.Women => "damer",
        RankingSection.Men => "herrar",
        _ => string.Empty,
    };

    /// <summary>"14 / 67" — placement out of the field.</summary>
    public static string PlaceOf(int? place, int starters) =>
        place is { } p ? $"{p} / {starters}" : $"— / {starters}";

    /// <summary>
    /// "33 av 67" — placeringen med fältet den vanns i. Ändelsen ":e" säger ingenting som
    /// talet inte redan säger; fältets storlek är det som gör placeringen läsbar. Utan känt
    /// fält står talet ensamt.
    /// </summary>
    public static string PlaceAmong(int? place, int starters) => place is { } p
        ? starters > 0 ? $"{p} av {starters}" : p.ToString(Sv)
        : "—";

    /// <summary>
    /// "5:21" — tiden per kilometer. Tom sträng utan känd banlängd: ett snitt räknat mot en
    /// gissad nämnare är ett påstående appen inte kan stå för.
    /// </summary>
    public static string Pace(TimeSpan time, double lengthKm) => lengthKm > 0
        ? Time(TimeSpan.FromSeconds(time.TotalSeconds / lengthKm))
        : string.Empty;

    public static string Clock(DateTimeOffset instant) => instant.ToString("HH:mm", Sv);

    /// <summary>
    /// How to say hello at this hour: "God morgon", "Hej", "God kväll".
    /// </summary>
    /// <remarks>
    /// Morning starts at four. An orienteer up at half past four is not up early by accident —
    /// they are driving to a start — and being greeted with "Hej" at that hour reads as an app
    /// that has not noticed.
    /// <para>
    /// The small hours get the neutral word rather than "God natt", which is a goodbye and not a
    /// greeting, and would be exactly wrong said to somebody on their way to a night race.
    /// </para>
    /// </remarks>
    public static string Salutation(DateTimeOffset instant) => instant.Hour switch
    {
        >= 4 and < 10 => "God morgon",
        >= 17 and < 23 => "God kväll",
        _ => "Hej",
    };

    /// <summary>"idag", "imorgon", "lör 15 aug" — dates as a person would say them.</summary>
    public static string RelativeDate(DateOnly date, DateOnly today)
    {
        int days = date.DayNumber - today.DayNumber;

        return days switch
        {
            0 => "idag",
            1 => "imorgon",
            -1 => "igår",
            > 1 and <= 6 => date.ToString("dddd", Sv),
            _ => date.ToString("ddd d MMM", Sv),
        };
    }

    /// <summary>
    /// A deadline in full: the weekday, the date, and how long that leaves.
    /// </summary>
    /// <remarks>
    /// "Anmälan stänger torsdag" is a weekday without a date, and a weekday alone is the one form
    /// a reader cannot act on — it could be this week or next, and the difference is whether there
    /// is still time. The countdown is the part that answers "do I have to do this now"; the date
    /// is the part they can put in a calendar. Both, or neither is enough.
    /// </remarks>
    public static string Deadline(DateOnly date, DateOnly today)
    {
        int days = date.DayNumber - today.DayNumber;

        string when = days switch
        {
            < 0 => "har stängt",
            0 => "idag",
            1 => "imorgon",
            _ => $"om {days} dagar",
        };

        // The month only, from the same abbreviation the rest of the app uses — it drops the
        // trailing period that eight of the twelve Swedish months otherwise carry.
        string month = date.ToString("MMM", Sv).TrimEnd('.');

        // Plain cardinal, not an ordinal: Swedish writes dates as "torsdag 20 augusti". The
        // ordinal form ("20:e") belongs to speech and to a day named on its own, and the test run
        // read "torsdag 20:e aug" as a bug in the formatting rather than as a date.
        return $"{date.ToString("dddd", Sv)} {date.Day} {month} ({when})";
    }

    /// <summary>
    /// A short date to put inside a sentence: "19 sep" where <c>d MMM</c> gives "19 sep.".
    /// </summary>
    /// <remarks>
    /// Eight of the twelve Swedish months abbreviate with a period of their own — mars, maj, juni
    /// and juli do not. A sentence that ends in a date therefore reads "faller ur 19 sep.." two
    /// thirds of the year and correctly the rest, which is how #115 got through review. The
    /// sentence keeps its own full stop; the date gives up the abbreviation's.
    /// </remarks>
    public static string DateInSentence(DateOnly date) =>
        date.ToString("d MMM", Sv).TrimEnd('.');

    /// <summary>The day number alone — the top line of a list's date column.</summary>
    public static string DayNumber(DateOnly date) => date.Day.ToString(Sv);

    /// <summary>"mån" — the weekday under the day number.</summary>
    public static string Weekday(DateOnly date) => date.ToString("ddd", Sv).TrimEnd('.');

    /// <summary>"aug" — the month under the weekday, for the row where it changes.</summary>
    public static string MonthShort(DateOnly date) => date.ToString("MMM", Sv).TrimEnd('.');

    /// <summary>"4–9 aug" for a range, a single date otherwise.</summary>
    public static string DateRange(DateOnly first, DateOnly last)
    {
        if (first == last)
            return first.ToString("d MMM", Sv);

        return first.Month == last.Month
            ? $"{first.Day}–{last.ToString("d MMM", Sv)}"
            : $"{first.ToString("d MMM", Sv)}–{last.ToString("d MMM", Sv)}";
    }

    /// <summary>
    /// An age class — D21, H45 — as against a course, which is what everything else is.
    /// </summary>
    /// <remarks>
    /// The two live in the same field on a result, and the list showed them in the same position
    /// without saying which was which: H21, H45, "Svår", "Blå 4 km", four ideas in one column.
    /// The shape is the only thing that tells them apart, and it is the same shape the class
    /// picker groups by, so the rule lives here rather than in each of them.
    /// </remarks>
    public static bool IsAgeClass(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^[DHdh]\s?\d{1,3}$");

    /// <summary>A class said as itself, a course said as a course.</summary>
    public static string ClassOrCourse(string name) =>
        string.IsNullOrWhiteSpace(name) || IsAgeClass(name) ? name : $"bana: {name}";

    /// <summary>"12 sek", "3 min" — how stale the live data is.</summary>
    public static string Age(TimeSpan age) =>
        age.TotalMinutes < 1
            ? $"{Math.Max(0, (int)age.TotalSeconds)} sek"
            : $"{(int)age.TotalMinutes} min";

    public static string Discipline(Discipline discipline) => discipline switch
    {
        Domain.Discipline.Sprint => "Sprint",
        Domain.Discipline.Middle => "Medel",
        Domain.Discipline.Long => "Lång",
        Domain.Discipline.UltraLong => "Ultralång",
        Domain.Discipline.Night => "Natt",
        Domain.Discipline.Relay => "Stafett",
        _ => string.Empty,
    };

    /// <summary>
    /// The sport in the words the federation uses. <see cref="Sport.Foot"/> has none: it is what
    /// a competition is unless it says otherwise, and writing "OL" on nine rows in ten is the
    /// noise the sport column exists to remove.
    /// </summary>
    public static string Sport(Sport sport) => sport switch
    {
        Domain.Sport.Indoor => "Indoor",
        Domain.Sport.MountainBike => "MTBO",
        Domain.Sport.Ski => "Skid-O",
        Domain.Sport.PreO => "PreO",
        Domain.Sport.Shooting => "Orienteringsskytte",
        _ => string.Empty,
    };

    /// <summary>
    /// The sport with a word for foot orienteering too. A row leaves it out because it is the
    /// default; a chip that offers the choice has to be able to name it.
    /// </summary>
    public static string SportOrDefault(Sport sport) =>
        sport == Domain.Sport.Foot ? "Orienteringslöpning" : Sport(sport);

    /// <summary>
    /// A kind of race in the words a runner would use: "Medel", "MTBO lång", "Indoor".
    /// </summary>
    /// <remarks>
    /// The distance is the word, and the sport is said only when it is in question — foot
    /// orienteering is what a race is unless stated. A sport with no distances is the whole
    /// answer on its own.
    /// </remarks>
    public static string RacePreference(RacePreference preference)
    {
        if (preference.Discipline is not { } distance)
            return SportOrDefault(preference.Sport);

        string word = Discipline(distance);

        return preference.Sport == Domain.Sport.Foot
            ? word
            : $"{Sport(preference.Sport)} {word.ToLower(Sv)}";
    }

    public static string Level(CompetitionLevel level) => level switch
    {
        CompetitionLevel.International => "Internationell",
        CompetitionLevel.Championship => "Mästerskap",
        CompetitionLevel.National => "Nationell",
        CompetitionLevel.District => "Distrikt",
        CompetitionLevel.Local => "Närtävling",
        CompetitionLevel.Training => "Träning",
        CompetitionLevel.Recreational => "Motion",
        _ => string.Empty,
    };

    public static string ResultStatus(ResultStatus status) => status switch
    {
        Domain.ResultStatus.Ok => "Godkänd",
        Domain.ResultStatus.Preliminary => "Preliminärt",
        Domain.ResultStatus.Mispunch => "Felstämplat",
        Domain.ResultStatus.DidNotFinish => "Bröt",
        Domain.ResultStatus.DidNotStart => "Ej start",
        _ => string.Empty,
    };

    public static string FollowReason(FollowReason reason) => reason switch
    {
        Domain.FollowReason.Family => "Familj",
        Domain.FollowReason.Clubmate => "Klubbkompis",
        Domain.FollowReason.Favourite => "Favorit",
        _ => string.Empty,
    };

    /// <summary>
    /// An em dash where there is no distance, the same as every other column in the app. A
    /// competition whose arena has no position must not claim a number for it.
    /// </summary>
    public static string Distance(double? kilometres) => kilometres is { } km ? Distance(km) : "—";

    /// <summary>"1,2 mil" reads better than "12 km" in Swedish sport contexts under 10 km.</summary>
    public static string Distance(double kilometres) =>
        kilometres < 10
            ? $"{kilometres.ToString("0.#", Sv)} km"
            : $"{kilometres.ToString("0", Sv)} km";
}
