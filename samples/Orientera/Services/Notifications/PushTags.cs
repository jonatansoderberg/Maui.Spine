using Orientera.Domain;

namespace Orientera.Services.Notifications;

/// <summary>
/// Orientera's notification preferences as the tags the backend targets. The backend never learns
/// who is entered in what — it only sends to a tag expression, and the phone decides which tags it
/// carries.
/// </summary>
public static class PushTags
{
    /// <summary>
    /// The kinds the backend can notice on its own and deliver as push. Everything else is a moment
    /// already present in the data, which the device schedules locally — <see cref="NotificationKind.TimeToLeave"/>
    /// most of all, since only the phone knows where it is.
    /// </summary>
    public static readonly IReadOnlySet<NotificationKind> Pushed = new HashSet<NotificationKind>
    {
        NotificationKind.ResultsPublished,
    };

    /// <summary>
    /// The tag for one kind, or <see langword="null"/> for the kinds no server can send: the ones
    /// computed on the device, and the ones with no data behind them yet.
    /// </summary>
    public static string? Tag(NotificationKind kind) => kind switch
    {
        NotificationKind.EntryClosing => "kind:entry-closing",
        NotificationKind.PmPublished => "kind:pm-published",
        NotificationKind.StartTimePublished => "kind:start-time-published",
        NotificationKind.LiveStarted => "kind:live-started",
        NotificationKind.ResultsPublished => "kind:results-published",
        _ => null,
    };

    /// <summary>
    /// The complete set of tags this installation should carry: what the user said yes to, who they
    /// are, which competitions they are entered in, and who in Min grupp they follow.
    /// </summary>
    /// <param name="preferences">What the user has turned on.</param>
    /// <param name="me">The signed-in runner.</param>
    /// <param name="competitions">The competitions the runner is entered in.</param>
    /// <param name="group">Min grupp, filtered to the ones notifications are on for.</param>
    /// <remarks>
    /// Everything but the kinds is optional, because everything but the kinds needs a source that
    /// may be unreachable. The kinds are a preference on this phone and nothing else, and a device
    /// registered for none of them hears nothing at all — which is a worse answer to "the calendar
    /// did not load" than registering for what the user asked for.
    /// </remarks>
    public static IReadOnlyList<string> For(
        NotificationPreferences preferences,
        PersonId? me = null,
        IEnumerable<CompetitionId>? competitions = null,
        IEnumerable<PersonId>? group = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var tags = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var kind in preferences.Enabled)
        {
            if (Tag(kind) is { } tag)
                tags.Add(tag);
        }

        // Without a kind there is nothing to send, and the rest of the tags would only make this
        // installation findable for a send it does not want.
        if (tags.Count == 0)
            return [];

        if (me is { } runner)
            tags.Add($"user:{runner.Value}");

        foreach (var competition in competitions ?? [])
            tags.Add($"competition:{competition.Value}");

        foreach (var person in group ?? [])
            tags.Add($"person:{person.Value}");

        return [.. tags];
    }
}
