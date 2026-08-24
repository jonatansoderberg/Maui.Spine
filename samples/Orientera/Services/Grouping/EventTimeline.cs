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

    /// <summary>
    /// Whether a row draws its own date, given the date on the row above it. The date column is
    /// a spine, and a spine that says the same day on four rows running is a column of noise.
    /// The first row of a section has nothing above it and always draws.
    /// </summary>
    /// <remarks>
    /// One rule for both orderings the list has. A calendar collapses hard — most competition
    /// days carry several — and "Mest relevant" is ranked rather than dated and almost never
    /// collapses at all. Neither has to know about the other.
    /// </remarks>
    public static bool DrawsDate(DateOnly? above, DateOnly date) => above != date;

    /// <summary>
    /// The same rule one step up, for the month under the weekday. A bare day number is enough
    /// under a heading that says "September"; under "Mest relevant", which has neither a month
    /// in the heading nor an order to its dates, it is not.
    /// </summary>
    public static bool DrawsMonth(DateOnly? above, DateOnly date) =>
        above is not { } previous || previous.Month != date.Month || previous.Year != date.Year;

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
