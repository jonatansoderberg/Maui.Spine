using Orientera.Domain;
using Orientera.Services.Local;
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
public sealed class FakeDataSource(IClock _clock, LocalIdentityStore? _identity = null) : IOrienteraSource
{
    private readonly FakeDataset _data = FakeDataset.Instance;
    private readonly List<FollowedPerson> _myGroup = [.. FakeDataset.Instance.MyGroup];

    // Local interests, no account needed. In-memory for M0; SQLite from M1.
    private readonly HashSet<CompetitionId> _interests = [FakeDataset.DmSprintId, FakeDataset.HosttraffenId];

    // ---------------------------------------------------------------- IEventSource

    public Task<IReadOnlyList<Competition>> GetCompetitionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Competitions);

    public Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Competitions.FirstOrDefault(c => c.Id == id));

    public Task<Course?> GetCourseAsync(CompetitionId id, string className, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Courses.FirstOrDefault(c => c.Competition == id && c.Class == className));

    public Task<Series?> GetSeriesAsync(SeriesId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Series.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlySet<CompetitionId>> GetInterestsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<CompetitionId>>(_interests);

    public Task<bool> ToggleInterestAsync(CompetitionId competition, CancellationToken cancellationToken = default)
    {
        bool added = _interests.Add(competition);

        if (!added)
            _interests.Remove(competition);

        return Task.FromResult(added);
    }

    // ---------------------------------------------------------------- IPeopleSource

    public Task<Person> GetMeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Me);

    /// <summary>
    /// The seeded runner, wearing whatever name the user gave the identity sheet.
    /// </summary>
    /// <remarks>
    /// The demo used to ignore the identity entirely: the sheet saved, the profile did not
    /// change, and the same sheet behaved differently against a real backend (#75). It applies
    /// here too — but as a rename of the seeded runner rather than as a new person.
    ///
    /// That distinction is the whole design. The seeded season is built around one runner: her
    /// results, her splits, her group, her prediction. Introducing a second identity beside her
    /// would leave the user outside every result list in the demo, which looks broken in a new
    /// way. Keeping her id and changing what she is called makes the demo about whoever is
    /// holding the phone, which is what it is for.
    /// </remarks>
    private Person Me
    {
        get
        {
            var identity = _identity?.Current;

            return identity is { IsComplete: true }
                ? _data.Me with
                {
                    Name = identity.Name,
                    Club = identity.Club,
                    DefaultClass = identity.DefaultClass,
                }
                : _data.Me;
        }
    }

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

    public Task FollowAsync(Person person, FollowReason reason, CancellationToken cancellationToken = default)
    {
        if (_myGroup.All(f => f.Person.Id != person.Id))
            _myGroup.Add(new FollowedPerson { Person = person, Reason = reason });

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

    public Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(
        CompetitionId competition, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultsFor(competition));

    public Task<IReadOnlyList<CompetitionResult>> GetOwnResultsAsync(
        PersonId person, IReadOnlyList<CompetitionId> competitions, bool splits = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompetitionResult>>(
            [.. competitions.Distinct().SelectMany(ResultsFor).Where(r => r.Person == person)]);

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

    public Task<LiveSnapshot> GetSnapshotAsync(
        CompetitionId competition,
        string? className = null,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;

        var runs = className is null
            ? Runs(competition)
            : [.. Runs(competition).Where(r => r.Class == className)];
        var entries = new List<LiveEntry>(runs.Count);

        var radios = runs
            .GroupBy(r => r.Class)
            .ToDictionary(byClass => byClass.Key, byClass => RadioControls(byClass.First()));

        foreach (var run in runs)
        {
            var radio = radios[run.Class];
            var passings = Passings(runs, run, radio, now);
            bool finished = run.HasFinishedBy(now);

            var status = !run.HasStartedBy(now) ? LiveStatus.NotStarted
                : finished && run.Status == ResultStatus.Mispunch ? LiveStatus.Mispunch
                : finished ? LiveStatus.Finished
                : LiveStatus.Running;

            TimeSpan? winner = finished && run.Status == ResultStatus.Ok ? WinningTime(runs, run, now) : null;

            entries.Add(new LiveEntry
            {
                Person = run.Person.Id,
                Name = run.Person.Name,
                Club = run.Person.Club,
                ClubLogo = FakeClubBadges.For(run.Person.Club),
                Class = run.Class,
                StartTime = run.StartTime,
                Status = status,
                Passings = passings,
                Position = finished && run.Status == ResultStatus.Ok
                    ? PlaceAmongFinished(runs, run, now)
                    : passings.Count > 0 ? passings[^1].Place : null,
                FinishTime = finished ? run.TotalTime : null,
                FinalPlace = finished && run.Status == ResultStatus.Ok ? PlaceAmongFinished(runs, run, now) : null,
                FinishBehind = winner is { } best ? run.TotalTime - best : null,
            });
        }

        var snapshot = new LiveSnapshot
        {
            Competition = competition,
            GeneratedAt = now,
            Entries = entries,
            Controls = radios.ToDictionary(
                byClass => byClass.Key,
                byClass => (IReadOnlyList<LiveControl>)[.. byClass.Value.Select(s => new LiveControl
                {
                    Code = s.ControlNumber,
                    Name = s.ControlCode,
                })]),
        };

        return Task.FromResult(snapshot);
    }

    // ---------------------------------------------------------------- ILiveloxSource

    /// <summary>
    /// Nothing. The seeded competitions do not exist in Livelox, and a link that opens somebody
    /// else's real event from a made-up one would be the demo lying about the outside world.
    /// </summary>
    public Task<LiveloxLink?> GetLiveloxAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Task.FromResult<LiveloxLink?>(null);

    /// <summary>
    /// Ingen. De seedade tävlingarna har ingen verklig arena att rendera, och hjälten faller
    /// tillbaka på terrängbilden — precis som för en riktig tävling vars bild inte blivit till.
    /// </summary>
    public Task<ArenaImage?> GetArenaImageAsync(CompetitionId competition, CancellationToken cancellationToken = default) =>
        Task.FromResult<ArenaImage?>(null);

    // ---------------------------------------------------------------- IProgressSource

    public Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.Ranking.Person == person ? _data.Ranking : null);

    public Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeriesStanding>>(
            _data.SeriesStandings.Where(s => s.Person == person).ToList());

    // ---------------------------------------------------------------- IClubActivitySource

    public Task<IReadOnlyList<ClubActivity>> GetClubActivitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_data.ClubActivities);

    // ---------------------------------------------------------------- IStartFieldSource

    /// <summary>
    /// The demo's field, ranked. The points are derived from each runner's seeded time so the
    /// list agrees with the race it belongs to — a demo that ranks people the opposite way to
    /// how they finish teaches the wrong thing about what the list means.
    /// </summary>
    /// <summary>
    /// The demo draws every competition the moment it is seeded, so there is never a stretch
    /// where entries exist and start times do not. The list the app would show before the draw is
    /// the start field itself.
    /// </summary>
    public Task<IReadOnlyList<StartFieldRunner>> GetEntryListAsync(
        CompetitionId competition, string className, CancellationToken cancellationToken = default) =>
        GetStartFieldAsync(competition, className, cancellationToken);

    public Task<IReadOnlyList<StartFieldRunner>> GetStartFieldAsync(
        CompetitionId competition, string className, CancellationToken cancellationToken = default)
    {
        if (!_data.Runs.TryGetValue(competition, out var runs))
            return Task.FromResult<IReadOnlyList<StartFieldRunner>>([]);

        var field = runs
            .Where(r => r.Class == className)
            .OrderBy(r => r.TotalTime)
            .Select((run, index) => new StartFieldRunner
            {
                Person = run.Person.Id,
                Name = run.Person.Name,
                Club = run.Person.Club,
                StartTime = run.StartTime,
                // Every fourth runner is outside the list, as in a real field.
                Points = index % 4 == 3 ? null : Math.Round(3.4 + (index * 1.9), 2),
                NationalRank = index % 4 == 3 ? null : 40 + (index * 37),
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<StartFieldRunner>>(
            [.. field.OrderBy(r => r.Points ?? double.MaxValue).ThenBy(r => r.StartTime)]);
    }

    // ---------------------------------------------------------------- projections

    /// <summary>
    /// The seeded runs, with the user's own runner carrying the identity's name and club.
    /// </summary>
    /// <remarks>
    /// Results and live rows are built from <see cref="PlannedRun.Person"/>, and both are matched
    /// back to the user by name and club (SP-04). Renaming here is therefore the only place it
    /// has to happen for the whole demo to agree on who the user is.
    /// </remarks>
    private IReadOnlyList<PlannedRun> Runs(CompetitionId competition)
    {
        if (!_data.Runs.TryGetValue(competition, out var runs))
            return [];

        var me = Me;

        return me == _data.Me
            ? runs
            : [.. runs.Select(r => r.Person.Id == _data.Me.Id ? r with { Person = me } : r)];
    }

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
                    ClubLogo = FakeClubBadges.For(run.Person.Club),
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

    /// <summary>
    /// The controls the class' course has a radio at. A real competition puts them at a couple
    /// of controls along the way, never at all of them — every third control is that shape, and
    /// the last control is the finish rather than a radio.
    /// </summary>
    private static IReadOnlyList<Split> RadioControls(PlannedRun run) =>
        [.. run.Splits.Where(s => s.ControlNumber % 3 == 0 && s.ControlNumber != run.Splits[^1].ControlNumber)];

    /// <summary>The radio controls this run had reached by <paramref name="now"/>.</summary>
    private static IReadOnlyList<LivePassing> Passings(
        IReadOnlyList<PlannedRun> runs,
        PlannedRun run,
        IReadOnlyList<Split> radios,
        DateTimeOffset now)
    {
        var passings = new List<LivePassing>(radios.Count);

        foreach (var radio in radios)
        {
            var mine = run.Splits.FirstOrDefault(s => s.ControlNumber == radio.ControlNumber);

            if (mine is null || run.StartTime + mine.ElapsedTime > now)
                continue;

            // A run that will not be ranked has no place at a control either, which is what
            // LiveResults reports for a mispunch.
            var ranked = run.Status == ResultStatus.Ok
                ? Elapsed(runs, run.Class, radio.ControlNumber, now).ToList()
                : [];

            passings.Add(new LivePassing
            {
                Control = mine.ControlNumber,
                Elapsed = mine.ElapsedTime,
                Place = ranked.Count > 0 ? ranked.Count(e => e < mine.ElapsedTime) + 1 : null,
                Behind = ranked.Count > 0 ? mine.ElapsedTime - ranked.Min() : null,
            });
        }

        return passings;
    }

    /// <summary>Every ranked time recorded at one control in one class so far.</summary>
    private static IEnumerable<TimeSpan> Elapsed(
        IReadOnlyList<PlannedRun> runs,
        string className,
        int control,
        DateTimeOffset now)
    {
        foreach (var run in runs)
        {
            if (run.Class != className || run.Status != ResultStatus.Ok)
                continue;

            var split = run.Splits.FirstOrDefault(s => s.ControlNumber == control);

            if (split is not null && run.StartTime + split.ElapsedTime <= now)
                yield return split.ElapsedTime;
        }
    }

    private static TimeSpan WinningTime(IReadOnlyList<PlannedRun> runs, PlannedRun run, DateTimeOffset now) =>
        runs
            .Where(other => other.Class == run.Class && other.Status == ResultStatus.Ok && other.HasFinishedBy(now))
            .Select(other => other.TotalTime)
            .DefaultIfEmpty()
            .Min();

    private static int PlaceAmongFinished(IReadOnlyList<PlannedRun> runs, PlannedRun run, DateTimeOffset now) =>
        runs.Count(other =>
            other.Class == run.Class
            && other.Status == ResultStatus.Ok
            && other.HasFinishedBy(now)
            && other.TotalTime < run.TotalTime) + 1;
}
