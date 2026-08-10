using System.Net;
using System.Text;
using System.Text.Json;
using Orientera.Services.FakeData;
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
            _local);

        await Assert.ThrowsAsync<SourceUnavailableException>(() => source.GetCompetitionsAsync());
    }

    /// <summary>Favourites and Min grupp are local by principle: no account, no connection needed.</summary>
    [Fact]
    public async Task Local_data_answers_even_when_the_backend_does_not()
    {
        var source = new BackendSource(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local);

        Assert.NotEmpty(await source.GetFavouritesAsync());
        Assert.NotEmpty(await source.GetMyGroupAsync());
        Assert.Equal(FakeDataset.Instance.Me.Name, (await source.GetMeAsync()).Name);
    }

    /// <summary>
    /// My entries need an identified person, which is M2. Until then the honest answer is
    /// nothing at all — a fabricated entry next to a real calendar would be worse.
    /// </summary>
    [Fact]
    public async Task What_is_not_integrated_yet_is_empty_rather_than_invented()
    {
        var source = SourceReturning(HttpStatusCode.OK, "[]");

        Assert.Empty(await source.GetEntriesAsync());
        Assert.Empty(await source.GetLiveCompetitionsAsync());
        Assert.Null(await source.GetRankingAsync(FakeDataset.Instance.Me.Id));
        Assert.Null(await source.GetPredictionAsync(Sprint.Id, FakeDataset.Instance.Me.Id));
    }

    private BackendSource SourceReturning(HttpStatusCode status, string body) =>
        new(
            new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("http://localhost:7071/api/") },
            _local);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, OrienteraJson.Options);

    private sealed class StubHandler(HttpStatusCode _status, string _body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}
