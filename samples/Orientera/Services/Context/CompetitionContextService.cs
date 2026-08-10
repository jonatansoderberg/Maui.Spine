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
    public async Task<ContextDecision> EvaluateAsync(
        Competition competition,
        CancellationToken cancellationToken = default) =>
        ContextEngine.Evaluate(await BuildInputAsync(competition, cancellationToken));

    public async Task<ContextInput> BuildInputAsync(
        Competition competition,
        CancellationToken cancellationToken = default)
    {
        var me = await _people.GetMeAsync(cancellationToken);
        var group = await _people.GetMyGroupAsync(cancellationToken);
        var entries = await _participation.GetEntriesAsync(cancellationToken);

        var groupIds = group.Select(f => f.Person.Id).ToHashSet();

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
