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

    /// <summary>"48:07" under an hour, "1:04:59" above it.</summary>
    public static string Time(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    public static string Time(TimeSpan? time) => time is { } t ? Time(t) : "—";

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

    /// <summary>"14 / 67" — placement out of the field.</summary>
    public static string PlaceOf(int? place, int starters) =>
        place is { } p ? $"{p} / {starters}" : $"— / {starters}";

    public static string Clock(DateTimeOffset instant) => instant.ToString("HH:mm", Sv);

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

    /// <summary>"4–9 aug" for a range, a single date otherwise.</summary>
    public static string DateRange(DateOnly first, DateOnly last)
    {
        if (first == last)
            return first.ToString("d MMM", Sv);

        return first.Month == last.Month
            ? $"{first.Day}–{last.ToString("d MMM", Sv)}"
            : $"{first.ToString("d MMM", Sv)}–{last.ToString("d MMM", Sv)}";
    }

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
        Domain.Discipline.Night => "Natt",
        Domain.Discipline.Relay => "Stafett",
        _ => string.Empty,
    };

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

    /// <summary>"1,2 mil" reads better than "12 km" in Swedish sport contexts under 10 km.</summary>
    public static string Distance(double kilometres) =>
        kilometres < 10
            ? $"{kilometres.ToString("0.#", Sv)} km"
            : $"{kilometres.ToString("0", Sv)} km";
}
