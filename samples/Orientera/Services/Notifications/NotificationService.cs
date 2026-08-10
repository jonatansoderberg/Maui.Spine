using Orientera.Domain;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Services.Notifications;

/// <summary>
/// Keeps the device's pending notifications equal to what the current data implies. Run on
/// start and on resume: what a runner needs to be told changes when the entry list, the start
/// list or their own plans do.
/// </summary>
public sealed class NotificationService(
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    NotificationPreferencesStore _preferences,
    INotificationScheduler _scheduler)
{
    /// <summary>
    /// Rebuilds the plan and hands it to the platform. Failures are swallowed on purpose: a
    /// source that cannot be reached must cost the user their notifications, not their app.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_scheduler.IsSupported)
            return;

        if (!_preferences.Current.Any)
        {
            await _scheduler.CancelAllAsync(cancellationToken);
            return;
        }

        try
        {
            await _scheduler.SyncAsync(await BuildAsync(cancellationToken), cancellationToken);
        }
        catch (SourceUnavailableException)
        {
            // Nothing to re-plan from. The notifications already scheduled stand.
        }
    }

    public async Task<IReadOnlyList<PlannedNotification>> BuildAsync(CancellationToken cancellationToken = default)
    {
        var me = await _people.GetMeAsync(cancellationToken);
        var group = await _people.GetMyGroupAsync(cancellationToken);
        var competitions = await _events.GetCompetitionsAsync(cancellationToken);
        var favourites = await _events.GetFavouritesAsync(cancellationToken);
        var entries = await _participation.GetEntriesAsync(cancellationToken);

        var groupIds = group
            .Where(f => f.NotificationsEnabled)
            .Select(f => f.Person.Id)
            .ToHashSet();

        var names = group.ToDictionary(f => f.Person.Id, f => f.Person.Name);

        var mine = entries.Where(e => e.Person == me.Id).Select(e => e.Competition).ToHashSet();

        var theirs = entries
            .Where(e => groupIds.Contains(e.Person))
            .GroupBy(e => e.Competition)
            .ToDictionary(g => g.Key, g => names[g.First().Person]);

        var starts = new Dictionary<CompetitionId, DateTimeOffset>();

        foreach (var competition in mine)
        {
            var start = (await _participation.GetStartsAsync(competition, cancellationToken))
                .FirstOrDefault(s => s.Person == me.Id);

            if (start is not null)
                starts[competition] = start.StartTime;
        }

        return NotificationPlanner.Plan(new NotificationContext
        {
            Now = _clock.Now,
            Me = me,
            Competitions = competitions,
            MyEntries = mine,
            GroupEntries = theirs,
            Favourites = favourites,
            MyStarts = starts,
            Preferences = _preferences.Current,
        });
    }
}
