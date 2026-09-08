using Orientera.Domain;

namespace Orientera.Backend.Push;

/// <summary>
/// Which competitions have results nobody has been told about yet. Pure, so the rule for what earns
/// a push — and what does not — is testable without a calendar or a register.
/// </summary>
public static class ResultsPublished
{
    /// <summary>
    /// How far back a publication still counts as news. Without a window the first run after a
    /// deploy would announce the whole back catalogue, all at once, to everyone.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The competitions to announce now, oldest publication first.</summary>
    /// <param name="competitions">The calendar as the backend currently reads it.</param>
    /// <param name="announced">The ones already sent, from the durable record.</param>
    /// <param name="now">The clock.</param>
    public static IReadOnlyList<Competition> Pending(
        IEnumerable<Competition> competitions,
        IReadOnlySet<CompetitionId> announced,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(competitions);
        ArgumentNullException.ThrowIfNull(announced);

        return
        [
            .. competitions
                .Where(c => c.Schedule.ResultsPublishedAt is { } at && at <= now && now - at <= Window)
                .Where(c => !announced.Contains(c.Id))
                .OrderBy(c => c.Schedule.ResultsPublishedAt)
        ];
    }
}
