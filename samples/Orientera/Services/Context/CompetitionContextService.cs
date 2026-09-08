using Orientera.Domain;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Services.Context;

/// <summary>
/// Assembles the personal half of <see cref="ContextInput"/> — my entry, my group's entries,
/// my start time — and hands it to the pure <see cref="ContextEngine"/>.
/// </summary>
public sealed class CompetitionContextService(
    IClock _clock,
    IPeopleSource _people,
    IParticipationSource _participation)
{
    /// <summary>
    /// The answers that do not change between competitions: who I am, who I follow, and what
    /// everyone is entered in.
    /// </summary>
    /// <remarks>
    /// Read once and passed along when a whole list is evaluated. Asked per competition it was
    /// three source calls per card, and a calendar of a thousand competitions made four thousand
    /// of them before the first row could be drawn.
    /// </remarks>
    public sealed record Audience(Person Me, IReadOnlySet<PersonId> Group, IReadOnlyList<CompetitionEntry> Entries);

    /// <summary>Reads the shared half once, for a pass over many competitions.</summary>
    public async Task<Audience> AudienceAsync(CancellationToken cancellationToken = default) =>
        new(await _people.GetMeAsync(cancellationToken),
            (await _people.GetMyGroupAsync(cancellationToken)).Select(f => f.Person.Id).ToHashSet(),
            await _participation.GetEntriesAsync(cancellationToken));

    public async Task<ContextDecision> EvaluateAsync(
        Competition competition,
        CancellationToken cancellationToken = default) =>
        await EvaluateAsync(competition, await AudienceAsync(cancellationToken), cancellationToken);

    /// <summary>Evaluates one competition against an audience already read.</summary>
    public async Task<ContextDecision> EvaluateAsync(
        Competition competition,
        Audience audience,
        CancellationToken cancellationToken = default) =>
        ContextEngine.Evaluate(await BuildInputAsync(competition, audience, cancellationToken));

    public async Task<ContextInput> BuildInputAsync(
        Competition competition,
        CancellationToken cancellationToken = default) =>
        await BuildInputAsync(competition, await AudienceAsync(cancellationToken), cancellationToken);

    public async Task<ContextInput> BuildInputAsync(
        Competition competition,
        Audience audience,
        CancellationToken cancellationToken = default)
    {
        var (me, groupIds, entries) = audience;

        var mine = entries.FirstOrDefault(e => e.Competition == competition.Id && e.Person == me.Id);

        var groupEntry = entries
            .Where(e => e.Competition == competition.Id && groupIds.Contains(e.Person))
            .OrderBy(e => e.RegisteredAt)
            .FirstOrDefault();

        DateTimeOffset? myStart = null;

        if (mine is not null)
        {
            var starts = await _participation.GetStartsAsync(competition.Id, cancellationToken);
            myStart = starts.FirstOrDefault(s => s.Person == me.Id)?.StartTime;
        }

        return new ContextInput
        {
            Now = _clock.Now,
            Competition = competition,
            MyEntryRegisteredAt = mine?.RegisteredAt,
            GroupEntryRegisteredAt = groupEntry?.RegisteredAt,
            MyStartTime = myStart,
        };
    }
}
