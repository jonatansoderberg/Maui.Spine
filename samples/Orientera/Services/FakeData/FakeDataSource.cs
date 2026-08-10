using Orientera.Domain;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Services.FakeData;

/// <summary>
/// The M0 data layer: every source interface served from <see cref="FakeDataset"/>.
/// </summary>
/// <remarks>
/// It reads the clock rather than the wall time, so results appear only once they have been
/// published and live positions follow the time machine. M1 replaces this with a BFF-backed
/// implementation behind the same interfaces; this one stays as demo and test mode.
/// </remarks>
public sealed class FakeDataSource(IClock _clock)
    : IEventSource, IPeopleSource, IParticipationSource, ILiveSource, IProgressSource
{
    private readonly FakeDataset _data = FakeDataset.Instance;
    private readonly List<FollowedPerson> _myGroup = [.. FakeDataset.Instance.MyGroup];

    // ---------------------------------------------------------------- IEventSource

    public Task<IReadOnlyList<Competition>> GetCompetitionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Competitions);

    public Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Competitions.FirstOrDefault(c => c.Id == id));

    public Task<Course?> GetCourseAsync(CompetitionId id, string className, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Courses.FirstOrDefault(c => c.Competition == id && c.Class == className));

    public Task<Series?> GetSeriesAsync(SeriesId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Series.FirstOrDefault(s => s.Id == id));

    // ---------------------------------------------------------------- IPeopleSource

    public Task<Person> GetMeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Me);

    public Task<IReadOnlyList<FollowedPerson>> GetMyGroupAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FollowedPerson>>([.. _myGroup]);

    public Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<Person>>([]);

        var matches = _data.People
            .Where(p => p.Id != _data.Me.Id)
            .Where(p => p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || p.Club.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();

        return Task.FromResult<IReadOnlyList<Person>>(matches);
    }

    public Task FollowAsync(PersonId person, FollowReason reason, CancellationToken cancellationToken = default)
    {
        if (_myGroup.Any(f => f.Person.Id == person))
            return Task.CompletedTask;

        var found = _data.People.FirstOrDefault(p => p.Id == person);
        if (found is not null)
            _myGroup.Add(new FollowedPerson { Person = found, Reason = reason });

        return Task.CompletedTask;
    }

    public Task UnfollowAsync(PersonId person, CancellationToken cancellationToken = default)
    {
        _myGroup.RemoveAll(f => f.Person.Id == person);
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- IParticipationSource

    public Task<IReadOnlyList<CompetitionEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var visible = _data.Entries.Where(e => e.RegisteredAt <= now).ToList();
        return Task.FromResult<IReadOnlyList<CompetitionEntry>>(visible);
    }

    public Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId competition, CancellationToken cancellationToken = default)
    {
        var found = _data.Competitions.FirstOrDefault(c => c.Id == competition);

        // A start list that has not been published yet does not exist for the user.
        if (found?.Schedule.StartListPublishedAt is not { } publishedAt || publishedAt > _clock.Now)
            return Task.FromResult<IReadOnlyList<Start>>([]);

        var starts = Runs(competition)
            .Select(r => new Start
            {
                Competition = competition,
                Person = r.Person.Id,
                Class = r.Class,
                StartTime = r.StartTime,
            })
            .OrderBy(s => s.StartTime)
            .ToList();

        return Task.FromResult<IReadOnlyList<Start>>(starts);
    }

    public Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultsFor(competition));

    public Task<IReadOnlyList<CompetitionResult>> GetResultsForPersonAsync(PersonId person, CancellationToken cancellationToken = default)
    {
        var results = _data.Runs.Keys
            .SelectMany(ResultsFor)
            .Where(r => r.Person == person)
            .ToList();

        var byDate = results
            .OrderByDescending(r => _data.Competitions.First(c => c.Id == r.Competition).FirstStart)
            .ToList();

        return Task.FromResult<IReadOnlyList<CompetitionResult>>(byDate);
    }

    public Task<Prediction?> GetPredictionAsync(CompetitionId competition, PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Predictions.FirstOrDefault(p => p.Competition == competition && p.Person == person));

    // ---------------------------------------------------------------- ILiveSource

    public Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;

        var live = _data.Competitions
            .Where(c => c.FirstStart <= now && now < c.LastFinish)
            .Where(c => _data.Runs.ContainsKey(c.Id))
            .OrderBy(c => c.FirstStart)
            .ToList();

        return Task.FromResult<IReadOnlyList<Competition>>(live);
    }

    public Task<LiveSnapshot> GetSnapshotAsync(CompetitionId competition, CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var runs = Runs(competition);
        var entries = new List<LiveEntry>(runs.Count);

        foreach (var run in runs)
        {
            var passed = run.Splits.LastOrDefault(s => run.StartTime + s.ElapsedTime <= now);
            bool finished = run.HasFinishedBy(now);

            var status = !run.HasStartedBy(now) ? LiveStatus.NotStarted
                : finished && run.Status == ResultStatus.Mispunch ? LiveStatus.Mispunch
                : finished ? LiveStatus.Finished
                : LiveStatus.Running;

            entries.Add(new LiveEntry
            {
                Person = run.Person.Id,
                Name = run.Person.Name,
                Club = run.Person.Club,
                Class = run.Class,
                StartTime = run.StartTime,
                Status = status,
                LastControlNumber = passed?.ControlNumber,
                ElapsedAtLastControl = passed?.ElapsedTime,
                Position = passed is null ? null : PositionAtControl(runs, run, passed, now),
                FinishTime = finished ? run.TotalTime : null,
                FinalPlace = finished && run.Status == ResultStatus.Ok ? PlaceAmongFinished(runs, run, now) : null,
            });
        }

        var snapshot = new LiveSnapshot
        {
            Competition = competition,
            GeneratedAt = now,
            Entries = entries,
        };

        return Task.FromResult(snapshot);
    }

    // ---------------------------------------------------------------- IProgressSource

    public Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Ranking.Person == person ? _data.Ranking : null);

    public Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeriesStanding>>(
            _data.SeriesStandings.Where(s => s.Person == person).ToList());

    // ---------------------------------------------------------------- projections

    private IReadOnlyList<PlannedRun> Runs(CompetitionId competition) =>
        _data.Runs.TryGetValue(competition, out var runs) ? runs : [];

    /// <summary>
    /// Results as they exist at the current time: nothing before anyone finishes, a
    /// preliminary list while the competition runs, and the ranked list once published.
    /// </summary>
    private IReadOnlyList<CompetitionResult> ResultsFor(CompetitionId competition)
    {
        var now = _clock.Now;
        var found = _data.Competitions.FirstOrDefault(c => c.Id == competition);

        if (found is null)
            return [];

        bool published = found.Schedule.ResultsPublishedAt is { } at && at <= now;
        bool splitsPublished = found.Schedule.SplitsPublishedAt is { } splitsAt && splitsAt <= now;

        var finished = Runs(competition).Where(r => r.HasFinishedBy(now)).ToList();

        if (finished.Count == 0)
            return [];

        var results = new List<CompetitionResult>(finished.Count);

        foreach (var byClass in finished.GroupBy(r => r.Class))
        {
            var ranked = byClass
                .Where(r => r.Status == ResultStatus.Ok)
                .OrderBy(r => r.TotalTime)
                .ToList();

            var winnerTime = ranked.Count > 0 ? ranked[0].TotalTime : TimeSpan.Zero;
            int starters = Runs(competition).Count(r => r.Class == byClass.Key);

            foreach (var run in byClass)
            {
                int? place = run.Status == ResultStatus.Ok ? ranked.IndexOf(run) + 1 : null;

                results.Add(new CompetitionResult
                {
                    Id = new ResultId($"{competition.Value}|{run.Person.Id.Value}"),
                    Competition = competition,
                    Person = run.Person.Id,
                    Name = run.Person.Name,
                    Club = run.Person.Club,
                    Class = run.Class,
                    Status = run.Status == ResultStatus.Ok && !published ? ResultStatus.Preliminary : run.Status,
                    Time = run.TotalTime,
                    Place = published ? place : null,
                    BehindWinner = published && run.Status == ResultStatus.Ok ? run.TotalTime - winnerTime : null,
                    Starters = starters,

                    // Splits are their own publication step — analysis unlocks after the result.
                    Splits = splitsPublished ? run.Splits : [],
                });
            }
        }

        return results
            .OrderBy(r => r.Class, StringComparer.Ordinal)
            .ThenBy(r => r.Place ?? int.MaxValue)
            .ThenBy(r => r.Time)
            .ToList();
    }

    /// <summary>Provisional position in the class at the runner's own last control.</summary>
    private static int PositionAtControl(
        IReadOnlyList<PlannedRun> runs,
        PlannedRun run,
        Split passed,
        DateTimeOffset now)
    {
        int ahead = 0;

        foreach (var other in runs)
        {
            if (other.Class != run.Class || ReferenceEquals(other, run) || other.Status != ResultStatus.Ok)
                continue;

            var theirSplit = other.Splits.FirstOrDefault(s => s.ControlNumber == passed.ControlNumber);

            if (theirSplit is null || other.StartTime + theirSplit.ElapsedTime > now)
                continue;

            if (theirSplit.ElapsedTime < passed.ElapsedTime)
                ahead++;
        }

        return ahead + 1;
    }

    private static int PlaceAmongFinished(IReadOnlyList<PlannedRun> runs, PlannedRun run, DateTimeOffset now) =>
        runs.Count(other =>
            other.Class == run.Class
            && other.Status == ResultStatus.Ok
            && other.HasFinishedBy(now)
            && other.TotalTime < run.TotalTime) + 1;
}
