using Orientera.Domain;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Finds runners among the names the backend has already fetched.
/// </summary>
/// <remarks>
/// Eventor has no public person lookup — resolving a person needs organisation rights, the same
/// wall the identity goes through (M5). What the backend <em>has</em> seen is real: the result
/// lists of the competitions in the calendar window. Searching those is a real search over real
/// people, needs no new access, and fetches nothing that was not going to be fetched anyway.
///
/// Result lists only. Eventor's start lists carry a person id, a class and a time, but no name
/// and no club — there is nothing in them to search. So a runner is findable once they have
/// finished a race in the window, and not before, which the app says rather than hides.
///
/// The bound matters. It searches a fixed number of competitions nearest today rather than the
/// whole window, so a search cannot turn into a sweep of the federation's season.
/// </remarks>
public sealed class PeopleSearch(EventorSource _events)
{
    /// <summary>How many competitions a single search may look through.</summary>
    private const int CompetitionBudget = 12;

    /// <summary>Enough that a search means something; below it every list matches.</summary>
    private const int ShortestQuery = 2;

    private const int MostResults = 40;

    public async Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Trim().Length < ShortestQuery)
            return [];

        var competitions = await _events.GetCompetitionsAsync(cancellationToken: cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Nearest today in either direction: a runner is looked for around the races being
        // followed, and those are the lists most likely to be cached already.
        var nearest = competitions
            .OrderBy(c => Math.Abs(c.Date.DayNumber - today.DayNumber))
            .Take(CompetitionBudget)
            .ToList();

        var found = new Dictionary<string, Person>(StringComparer.Ordinal);

        foreach (var competition in nearest)
        {
            foreach (var person in await PeopleInAsync(competition, cancellationToken))
            {
                if (!Matches(person, query))
                    continue;

                // Name and club is the identity that spans systems (SP-04); the same runner in
                // two competitions is one row in the answer.
                found.TryAdd(RunnerIdentity.Of(person.Name, person.Club).ToString(), person);
            }

            if (found.Count >= MostResults)
                break;
        }

        return [.. found.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture).Take(MostResults)];
    }

    private static bool Matches(Person person, string query) =>
        person.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || person.Club.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Whoever the result list names, through the cache the competition's page fills.</summary>
    private async Task<IEnumerable<Person>> PeopleInAsync(Competition competition, CancellationToken cancellationToken)
    {
        try
        {
            var results = await _events.GetResultsAsync(competition.Id, cancellationToken);

            return results.Select(r => Person(r.Person, r.Name, r.Club, r.Class, competition.District));
        }
        // One competition whose lists will not load is not a reason to answer nothing.
        catch (Exception)
        {
            return [];
        }
    }

    private static Person Person(PersonId id, string name, string club, string className, string district) => new()
    {
        Id = id,
        Name = name,
        Club = club,
        District = district,
        DefaultClass = className,
    };
}
