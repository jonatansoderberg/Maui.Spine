using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Services.Grouping;

/// <summary>
/// Which heading a competition belongs under.
/// </summary>
/// <remarks>
/// A series spans several dates, so the section is chosen from the date the runner would act on:
/// the first occurrence still ahead, or the last one if the whole series has been run. That keeps
/// "Veckans bana, 4–9 aug" in one place instead of splitting it across four headings.
/// </remarks>
public static class EventTimeline
{
    /// <summary>Past competitions are listed newest first — the one just run is the one being looked for.</summary>
    public const string Past = "Tidigare";

    /// <summary>What ran today or yesterday, kept in the list rather than archived.</summary>
    public const string Recent = "Nyligen";

    /// <summary>
    /// How far back a competition stays in the planning list. One day: the race you ran
    /// yesterday is what you are still looking up, and the summer before it is not.
    /// </summary>
    private const int RecentDays = 1;

    /// <summary>
    /// True for what belongs behind the "Tidigare" chip rather than in the list. Yesterday's
    /// races are not archived — a runner is still looking for their own result.
    /// </summary>
    public static bool IsPast(EventGroup group, DateOnly today) => group.LastDate < today.AddDays(-RecentDays);

    /// <summary>The date the group is filed under: what is still to come, or what last happened.</summary>
    public static DateOnly SortDate(EventGroup group, DateOnly today) =>
        group.Occurrences.Select(c => c.Date).Where(d => d >= today).DefaultIfEmpty(group.LastDate).Min();

    public static string NameFor(EventGroup group, DateOnly today)
    {
        if (IsPast(group, today))
            return Past;

        var date = SortDate(group, today);

        // Nothing ahead, but recent enough to keep: it ran yesterday.
        if (date < today)
            return Recent;

        // Weeks, then months. Nobody plans "in 23 days"; they plan this weekend, next weekend,
        // and then by month.
        if (date < today.AddDays(7))
            return "Denna vecka";

        if (date < today.AddDays(14))
            return "Nästa vecka";

        var month = date.ToString("MMMM", Format.Culture);

        return date.Year == today.Year
            ? char.ToUpper(month[0], Format.Culture) + month[1..]
            : $"{char.ToUpper(month[0], Format.Culture) + month[1..]} {date.Year}";
    }
}
