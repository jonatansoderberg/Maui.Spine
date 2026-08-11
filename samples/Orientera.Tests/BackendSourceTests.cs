using System.Net;
using System.Text;
using System.Text.Json;
using Orientera.Services.FakeData;
using Orientera.Services.Local;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Tests;

/// <summary>
/// The app against the BFF. What matters here is not that HTTP works, but that the app can
/// tell an empty answer from a missing source — and that what is local stays local.
/// </summary>
public class BackendSourceTests
{
    private readonly FakeDataSource _local = new(new TimeMachineClock(FakeDataset.DefaultNow));

    // No identity set: "me" is the seeded runner, which is what the app shows until the user
    // says who they are.
    private readonly LocalIdentityStore _identity = new(Path.Combine(
        Path.GetTempPath(), $"orientera-identity-{Guid.NewGuid():N}.json"));

    private readonly LocalGroupStore _group = new(Path.Combine(
        Path.GetTempPath(), $"orientera-group-{Guid.NewGuid():N}.json"));

    private static readonly Competition Sprint = new()
    {
        Id = new CompetitionId("38412"),
        Name = "DM, Sprint",
        Organiser = "Gävle OK",
        District = "Gästrikland",
        Place = "Gävle centrum",
        Location = new GeoPoint(60.6749, 17.1413),
        Discipline = Discipline.Sprint,
        Level = CompetitionLevel.District,
        FirstStart = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(2026, 8, 15, 15, 0, 0, TimeSpan.FromHours(2)),
        Classes = ["H21", "D21"],
    };

    [Fact]
    public async Task A_competition_survives_the_wire_unchanged()
    {
        var source = SourceReturning(HttpStatusCode.OK, Json<Competition[]>([Sprint]));

        var competition = Assert.Single(await source.GetCompetitionsAsync());

        // Field by field rather than record equality: the lists are equal in content but not
        // by reference, which is all a record compares them by.
        Assert.Equal(Sprint.Id, competition.Id);
        Assert.Equal(Sprint.Name, competition.Name);
        Assert.Equal(Sprint.Organiser, competition.Organiser);
        Assert.Equal(Sprint.District, competition.District);
        Assert.Equal(Sprint.Location, competition.Location);
        Assert.Equal(Sprint.Discipline, competition.Discipline);
        Assert.Equal(Sprint.Level, competition.Level);
        Assert.Equal(Sprint.FirstStart, competition.FirstStart);
        Assert.Equal(Sprint.LastFinish, competition.LastFinish);
        Assert.Equal(Sprint.Classes, competition.Classes);
    }

    /// <summary>An id is a string on the wire — the contract is read by more than C#.</summary>
    [Fact]
    public void An_id_is_written_as_a_plain_string() =>
        Assert.Contains("\"id\":\"38412\"", Json(Sprint));

    [Fact]
    public async Task An_empty_calendar_is_an_answer_not_a_failure()
    {
        var source = SourceReturning(HttpStatusCode.OK, "[]");

        Assert.Empty(await source.GetCompetitionsAsync());
    }

    [Fact]
    public async Task A_competition_that_does_not_exist_is_null()
    {
        var source = SourceReturning(HttpStatusCode.NotFound, string.Empty);

        Assert.Null(await source.GetCompetitionAsync(new CompetitionId("38412")));
    }

    /// <summary>
    /// The backend answers 502 when Eventor is down. That has to arrive as the same failure
    /// the offline package already listens for, or the fallback never runs.
    /// </summary>
    [Fact]
    public async Task A_source_that_is_down_becomes_the_failure_the_fallback_catches()
    {
        var source = SourceReturning(HttpStatusCode.BadGateway, """{"error":"source_unavailable"}""");

        await Assert.ThrowsAsync<SourceUnavailableException>(() => source.GetCompetitionsAsync());
    }

    [Fact]
    public async Task A_backend_that_cannot_be_reached_becomes_the_same_failure()
    {
        var source = new BackendSource(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local,
            _identity,
            _group);

        await Assert.ThrowsAsync<SourceUnavailableException>(() => source.GetCompetitionsAsync());
    }

    /// <summary>Favourites and identity are local by principle: no account, no connection needed.</summary>
    [Fact]
    public async Task Local_data_answers_even_when_the_backend_does_not()
    {
        var source = Offline();

        Assert.NotEmpty(await source.GetFavouritesAsync());
        Assert.Equal(FakeDataset.Instance.Me.Name, (await source.GetMeAsync()).Name);
    }

    /// <summary>
    /// Min grupp is local too, but against a real backend it starts empty. The demo dataset's
    /// three followed runners belong to the demo; a real runner opening the app to find three
    /// strangers they never chose is the app inventing a social graph (#63).
    /// </summary>
    [Fact]
    public async Task My_group_starts_empty_and_holds_only_what_the_user_followed()
    {
        var source = Offline();

        Assert.Empty(await source.GetMyGroupAsync());

        var runner = new Person
        {
            Id = new PersonId("144299"),
            Name = "Johan Sjödin",
            Club = "Stora Tuna OK",
            District = "Dalarna",
            DefaultClass = "H21",
        };

        await source.FollowAsync(runner, FollowReason.Favourite);

        Assert.Equal("Johan Sjödin", Assert.Single(await source.GetMyGroupAsync()).Person.Name);

        await source.UnfollowAsync(runner.Id);

        Assert.Empty(await source.GetMyGroupAsync());
    }

    /// <summary>The seeded demo people are not searchable against a real backend.</summary>
    [Fact]
    public async Task Search_asks_the_backend_rather_than_the_demo_dataset()
    {
        var source = SourceReturning(HttpStatusCode.OK, Json(new List<Person>()));

        Assert.Empty(await source.SearchAsync("Alfred"));
    }

    private BackendSource Offline() =>
        new(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local,
            _identity,
            _group);

    /// <summary>
    /// My entries need an identified person, which is M2. Until then the honest answer is
    /// nothing at all — a fabricated entry next to a real calendar would be worse.
    ///
    /// Sverigelistan used to be on this list and no longer is: the backend fetches a real one
    /// through a runner's own Eventor session (issues/105-ranking-lookup.md).
    /// </summary>
    [Fact]
    public async Task What_is_not_integrated_yet_is_empty_rather_than_invented()
    {
        var source = SourceReturning(HttpStatusCode.OK, "[]");

        Assert.Empty(await source.GetEntriesAsync());
        Assert.Empty(await source.GetLiveCompetitionsAsync());
        Assert.Null(await source.GetPredictionAsync(Sprint.Id, FakeDataset.Instance.Me.Id));

    }

    /// <summary>
    /// The BFF leaves null members out of its JSON, so a runner without a start time arrives
    /// without the property at all. A contract that demands it turns that runner into "no
    /// connection" for the whole screen (#65).
    /// </summary>
    [Fact]
    public async Task A_live_entry_without_a_start_time_survives_the_wire()
    {
        var body = Json(new LiveSnapshot
        {
            Competition = Sprint.Id,
            GeneratedAt = Sprint.FirstStart,
            Entries =
            [
                new LiveEntry
                {
                    Person = new PersonId("maria falk|sundsvalls ok"),
                    Name = "Maria Falk",
                    Club = "Sundsvalls OK",
                    Class = "Blå 3,0",
                    StartTime = null,
                    Status = LiveStatus.NotStarted,
                },
            ],
        });

        Assert.DoesNotContain("startTime", body);

        var snapshot = await SourceReturning(HttpStatusCode.OK, body).GetSnapshotAsync(Sprint.Id);

        Assert.Null(Assert.Single(snapshot.Entries).StartTime);
    }

    /// <summary>
    /// A cold backend is the source being unavailable, not the caller changing its mind. The
    /// difference matters: an unavailable source hands over to the offline package, while a
    /// swallowed cancellation leaves an empty screen with nothing said (#51).
    /// </summary>
    [Fact]
    public async Task A_backend_too_slow_to_answer_is_an_unavailable_source()
    {
        var source = new BackendSource(
            new HttpClient(new SlowHandler()) { BaseAddress = new Uri("http://localhost:7071/api/"), Timeout = TimeSpan.FromMilliseconds(50) },
            _local,
            _identity,
            _group);

        await Assert.ThrowsAsync<SourceUnavailableException>(() => source.GetCompetitionsAsync());
    }

    /// <summary>The caller giving up is still the caller's business, and must not read as offline.</summary>
    [Fact]
    public async Task A_caller_that_gives_up_is_not_an_unavailable_source()
    {
        var source = new BackendSource(
            new HttpClient(new SlowHandler()) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local,
            _identity,
            _group);

        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetCompetitionsAsync(giveUp.Token));
    }

    private BackendSource SourceReturning(HttpStatusCode status, string body) =>
        new(
            new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local,
            _identity,
            _group);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, OrienteraJson.Options);

    private sealed class StubHandler(HttpStatusCode _status, string _body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>Never answers — the cold backend still downloading three thousand organisations.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}
