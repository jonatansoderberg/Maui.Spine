using Orientera.Domain;
using Orientera.Services.Travel;

namespace Orientera.Services.Notifications;

/// <summary>What the planner knows when it decides what to schedule.</summary>
public sealed record NotificationContext
{
    public required DateTimeOffset Now { get; init; }
    public required Person Me { get; init; }
    public required IReadOnlyList<Competition> Competitions { get; init; }

    /// <summary>Competitions I am entered in.</summary>
    public IReadOnlySet<CompetitionId> MyEntries { get; init; } = new HashSet<CompetitionId>();

    /// <summary>Competitions someone in Min grupp is entered in, and who.</summary>
    public IReadOnlyDictionary<CompetitionId, string> GroupEntries { get; init; } =
        new Dictionary<CompetitionId, string>();

    public IReadOnlySet<CompetitionId> Interests { get; init; } = new HashSet<CompetitionId>();

    /// <summary>My start time, where the start list has been drawn.</summary>
    public IReadOnlyDictionary<CompetitionId, DateTimeOffset> MyStarts { get; init; } =
        new Dictionary<CompetitionId, DateTimeOffset>();

    public NotificationPreferences Preferences { get; init; } = NotificationPreferences.Default;
}

/// <summary>
/// Decides which notifications should be scheduled, and when. Pure and clock-driven, like the
/// context engine — the same competition produces the same plan whenever it is asked.
/// </summary>
/// <remarks>
/// Everything here is a moment already present in the data: a deadline, a publication time, a
/// first start, a start time. That is the whole reason the groundwork can be local — nothing
/// needs a server to notice that it happened. Push, for the things only a server can know,
/// is M5.
/// </remarks>
public static class NotificationPlanner
{
    /// <summary>A deadline is worth a day's warning; an hour's is no help at all.</summary>
    public static readonly TimeSpan EntryWarning = TimeSpan.FromHours(24);

    public static IReadOnlyList<PlannedNotification> Plan(NotificationContext context)
    {
        var planned = new List<PlannedNotification>();

        foreach (var competition in context.Competitions)
        {
            bool mine = context.MyEntries.Contains(competition.Id);
            bool group = context.GroupEntries.ContainsKey(competition.Id);
            bool followed = mine || group || context.Interests.Contains(competition.Id);

            if (!followed)
                continue;

            // Being reminded that entry closes for a competition I have already entered is
            // noise; for one I am only watching, it is the point.
            if (!mine && competition.Schedule.EntryDeadline is { } deadline)
            {
                Add(competition, NotificationKind.EntryClosing, deadline - EntryWarning,
                    $"Anmälan stänger {Format(deadline)}.");
            }

            if (mine && competition.Documents.FirstOrDefault(d => d.Kind == DocumentKind.Pm)?.PublishedAt is { } pm)
            {
                Add(competition, NotificationKind.PmPublished, pm,
                    "PM är publicerat. Läs det viktigaste innan du åker.");
            }

            if (mine && competition.Schedule.StartListPublishedAt is { } startList)
            {
                Add(competition, NotificationKind.StartTimePublished, startList,
                    "Startlistan är publicerad.");
            }

            if (mine && context.MyStarts.TryGetValue(competition.Id, out var myStart))
            {
                var leave = TravelEstimate.LeaveAt(context.Me.Home, competition.Location, myStart);

                Add(competition, NotificationKind.TimeToLeave, leave,
                    $"Dags att åka. Din start går {Format(myStart)}, {Distance(context, competition)} härifrån.");
            }

            if (mine || group)
            {
                Add(competition, NotificationKind.LiveStarted, competition.FirstStart,
                    group && !mine
                        ? $"Första start har gått. {context.GroupEntries[competition.Id]} är med."
                        : "Första start har gått.");
            }

            if ((mine || group) && competition.Schedule.ResultsPublishedAt is { } results)
            {
                Add(competition, NotificationKind.ResultsPublished, results,
                    "Resultaten är publicerade.");
            }
        }

        return [.. planned.OrderBy(n => n.At)];

        void Add(Competition competition, NotificationKind kind, DateTimeOffset at, string body)
        {
            // A notification whose moment has passed is not a notification — it is a nag about
            // something the user already lived through.
            if (at <= context.Now || !context.Preferences.IsEnabled(kind))
                return;

            planned.Add(new PlannedNotification
            {
                Kind = kind,
                Competition = competition.Id,
                At = at,
                Title = competition.Name,
                Body = body,
            });
        }
    }

    private static string Distance(NotificationContext context, Competition competition) =>
        $"{TravelEstimate.DistanceKm(context.Me.Home, competition.Location):0} km";

    private static string Format(DateTimeOffset at) => at.ToString("dddd HH:mm");
}
