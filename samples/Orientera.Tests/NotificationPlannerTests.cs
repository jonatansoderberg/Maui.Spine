using Orientera.Services.Notifications;

namespace Orientera.Tests;

/// <summary>
/// Notifications are the one feature that reaches a runner when the app is closed, so the rules
/// for what earns an interruption — and what does not — are the feature.
/// </summary>
public class NotificationPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly Person Me = new()
    {
        Id = new PersonId("me"),
        Name = "Elin Nordqvist",
        Club = "Gävle OK",
        District = "Gästrikland",
        DefaultClass = "D21",
        Home = new GeoPoint(60.6749, 17.1413),
    };

    private static Competition Competition(
        string id = "38412",
        DateTimeOffset? firstStart = null,
        DateTimeOffset? entryDeadline = null,
        DateTimeOffset? startList = null,
        DateTimeOffset? results = null,
        DateTimeOffset? pm = null) => new()
    {
        Id = new CompetitionId(id),
        Name = "DM, Sprint",
        Organiser = "Gävle OK",
        District = "Gästrikland",
        Place = "Gävle centrum",
        Location = new GeoPoint(60.6749, 17.1413),
        Discipline = Discipline.Sprint,
        Level = CompetitionLevel.District,
        FirstStart = firstStart ?? Now.AddDays(5),
        LastFinish = (firstStart ?? Now.AddDays(5)).AddHours(5),
        Schedule = new CompetitionSchedule
        {
            EntryDeadline = entryDeadline,
            StartListPublishedAt = startList,
            ResultsPublishedAt = results,
        },
        Documents = pm is null
            ? []
            : [new CompetitionDocument { Kind = DocumentKind.Pm, Title = "PM", Url = "x", PublishedAt = pm }],
    };

    private static NotificationContext Context(
        Competition competition,
        bool entered = false,
        bool interested = false,
        (string Name, bool Enabled)? groupMember = null,
        DateTimeOffset? myStart = null,
        params NotificationKind[] enabled)
    {
        var preferences = NotificationPreferences.Default;

        foreach (var kind in enabled.Length > 0 ? enabled : [.. NotificationKinds.Available])
            preferences = preferences.With(kind, true);

        return new NotificationContext
        {
            Now = Now,
            Me = Me,
            Competitions = [competition],
            MyEntries = entered ? new HashSet<CompetitionId> { competition.Id } : [],
            Interests = interested ? new HashSet<CompetitionId> { competition.Id } : [],
            GroupEntries = groupMember is { Enabled: true } member
                ? new Dictionary<CompetitionId, string> { [competition.Id] = member.Name }
                : new Dictionary<CompetitionId, string>(),
            MyStarts = myStart is { } start
                ? new Dictionary<CompetitionId, DateTimeOffset> { [competition.Id] = start }
                : new Dictionary<CompetitionId, DateTimeOffset>(),
            Preferences = preferences,
        };
    }

    /// <summary>Nothing is scheduled until the user has asked for it. Opt-in, per type.</summary>
    [Fact]
    public void Nothing_is_planned_without_an_opt_in()
    {
        var context = Context(Competition(entryDeadline: Now.AddDays(3)), interested: true) with
        {
            Preferences = NotificationPreferences.Default,
        };

        Assert.Empty(NotificationPlanner.Plan(context));
    }

    /// <summary>A competition I have nothing to do with is not my business.</summary>
    [Fact]
    public void A_competition_I_do_not_follow_produces_nothing() =>
        Assert.Empty(NotificationPlanner.Plan(Context(Competition(entryDeadline: Now.AddDays(3)))));

    [Fact]
    public void An_entry_deadline_warns_a_day_ahead()
    {
        var deadline = Now.AddDays(3);

        var planned = NotificationPlanner.Plan(Context(Competition(entryDeadline: deadline), interested: true));

        var warning = Assert.Single(planned, n => n.Kind == NotificationKind.EntryClosing);

        Assert.Equal(deadline.AddHours(-24), warning.At);
    }

    /// <summary>Telling me that entry closes for a race I have already entered is noise.</summary>
    [Fact]
    public void An_entry_deadline_is_not_repeated_to_someone_already_entered()
    {
        var planned = NotificationPlanner.Plan(
            Context(Competition(entryDeadline: Now.AddDays(3)), entered: true));

        Assert.DoesNotContain(planned, n => n.Kind == NotificationKind.EntryClosing);
    }

    /// <summary>A moment that has passed is not a notification; it is a nag.</summary>
    [Fact]
    public void A_deadline_too_close_to_warn_about_is_dropped()
    {
        var planned = NotificationPlanner.Plan(
            Context(Competition(entryDeadline: Now.AddHours(6)), interested: true));

        Assert.DoesNotContain(planned, n => n.Kind == NotificationKind.EntryClosing);
    }

    [Fact]
    public void The_pm_and_the_start_list_are_announced_to_those_running()
    {
        var competition = Competition(pm: Now.AddDays(1), startList: Now.AddDays(2));

        var planned = NotificationPlanner.Plan(Context(competition, entered: true));

        Assert.Contains(planned, n => n.Kind == NotificationKind.PmPublished && n.At == Now.AddDays(1));
        Assert.Contains(planned, n => n.Kind == NotificationKind.StartTimePublished && n.At == Now.AddDays(2));
    }

    /// <summary>
    /// Leaving time is the start minus the margin at the arena minus the drive. Just under 70 km
    /// is far enough to be driven at the full road average, 80 km/h — 52 minutes — plus the
    /// 45-minute margin at the arena.
    /// </summary>
    [Fact]
    public void Time_to_leave_counts_backwards_from_my_start()
    {
        var start = Now.AddDays(5).AddHours(2);

        var competition = Competition() with { Location = new GeoPoint(61.3037, 17.1413) };

        var planned = NotificationPlanner.Plan(
            Context(competition, entered: true, myStart: start));

        var leave = Assert.Single(planned, n => n.Kind == NotificationKind.TimeToLeave);

        Assert.Equal(start.AddMinutes(-45).AddMinutes(-52), leave.At);
    }

    [Fact]
    public void Live_and_results_reach_the_group_too()
    {
        var competition = Competition(results: Now.AddDays(5).AddHours(6));

        var planned = NotificationPlanner.Plan(
            Context(competition, groupMember: ("Viktor Norberg", true)));

        var live = Assert.Single(planned, n => n.Kind == NotificationKind.LiveStarted);

        Assert.Contains("Viktor Norberg", live.Body);
        Assert.Contains(planned, n => n.Kind == NotificationKind.ResultsPublished);
    }

    /// <summary>Following someone is not the same as wanting to be woken by them.</summary>
    [Fact]
    public void A_group_member_without_notifications_enabled_is_not_a_reason_to_notify()
    {
        var planned = NotificationPlanner.Plan(
            Context(Competition(), groupMember: ("Viktor Norberg", false)));

        Assert.Empty(planned);
    }

    /// <summary>One type off means that type is gone, and the others stay.</summary>
    [Fact]
    public void Each_type_is_switched_on_its_own()
    {
        var competition = Competition(pm: Now.AddDays(1), startList: Now.AddDays(2));

        var planned = NotificationPlanner.Plan(
            Context(competition, entered: true, enabled: NotificationKind.PmPublished));

        var only = Assert.Single(planned);

        Assert.Equal(NotificationKind.PmPublished, only.Kind);
    }

    /// <summary>
    /// Re-planning has to replace, not stack: the id is derived from the type and the
    /// competition, so the same notification is the same notification.
    /// </summary>
    [Fact]
    public void The_same_notification_keeps_the_same_id()
    {
        var competition = Competition(pm: Now.AddDays(1));
        var context = Context(competition, entered: true);

        var first = NotificationPlanner.Plan(context).Single(n => n.Kind == NotificationKind.PmPublished);
        var second = NotificationPlanner.Plan(context).Single(n => n.Kind == NotificationKind.PmPublished);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("PmPublished:38412", first.Id);
    }

    [Fact]
    public void The_plan_comes_out_in_the_order_it_will_happen()
    {
        var competition = Competition(
            firstStart: Now.AddDays(5),
            entryDeadline: Now.AddDays(2),
            startList: Now.AddDays(4),
            results: Now.AddDays(5).AddHours(6),
            pm: Now.AddDays(3));

        var planned = NotificationPlanner.Plan(Context(competition, entered: true, interested: true));

        Assert.Equal(planned.OrderBy(n => n.At), planned);
    }
}
