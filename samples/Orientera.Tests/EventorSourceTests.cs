using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Backend.Upstream;

namespace Orientera.Tests;

/// <summary>
/// The whole backend path — request, cache, XML, normalisation — against an Eventor that
/// answers with the documented fixtures. What the BFF returns is what these produce.
/// </summary>
public class EventorSourceTests
{
    private readonly EventorStub _eventor = new();
    private readonly EventorSource _source;

    public EventorSourceTests()
    {
        var options = Options.Create(new EventorOptions
        {
            ApiKey = "test-key",
            BaseAddress = "https://eventor.example/api/",
            OrganisationIds = "10",
        });

        var client = new EventorClient(
            new HttpClient(_eventor) { BaseAddress = new Uri(options.Value.BaseAddress) },
            options,
            NullLogger<EventorClient>.Instance);

        _source = new EventorSource(client, new ResponseCache(new MemoryCache(new MemoryCacheOptions())), options);
    }

    [Fact]
    public async Task The_calendar_comes_back_normalised()
    {
        var competitions = await _source.GetCompetitionsAsync();

        Assert.Equal(7, competitions.Count);
        Assert.Equal("Norrlandsmästerskapen, medel", competitions[0].Name);
        Assert.Equal("Gästrikland", competitions[0].District);
    }

    [Fact]
    public async Task The_key_travels_in_the_header_and_nowhere_else()
    {
        await _source.GetCompetitionsAsync();

        Assert.All(_eventor.Requests, request =>
        {
            Assert.Equal("test-key", request.Headers.GetValues("ApiKey").Single());
            Assert.DoesNotContain("test-key", request.RequestUri!.Query);
        });
    }

    [Fact]
    public async Task The_calendar_window_and_the_district_are_asked_for()
    {
        await _source.GetCompetitionsAsync(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));

        var query = _eventor.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/events")).RequestUri!.Query;

        // Eventor takes input in UTC, and a Swedish August day starts two hours earlier there.
        Assert.Contains("fromDate=2026-07-31%2022%3A00%3A00", query);
        Assert.Contains("toDate=2026-08-31%2021%3A59%3A59", query);
        Assert.Contains("organisationIds=10", query);
        Assert.Contains("includeEntryBreaks=true", query);
    }

    [Fact]
    public async Task The_same_calendar_is_only_fetched_once()
    {
        await _source.GetCompetitionsAsync();
        await _source.GetCompetitionsAsync();

        Assert.Equal(1, _eventor.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/events")));
    }

    /// <summary>The detail view is where the documents and classes are attached.</summary>
    [Fact]
    public async Task A_competition_detail_carries_its_documents_and_classes()
    {
        var competition = await _source.GetCompetitionAsync(new CompetitionId("38499"));

        Assert.NotNull(competition);
        Assert.Equal("Natt-SM, långdistans", competition.Name);
        Assert.Equal("PM Natt-SM", Assert.Single(competition.Documents).Title);
        Assert.Equal(["H20", "H21", "D21", "Herrar 45 år"], competition.Classes);
    }

    /// <summary>
    /// Splits are published after the results and the calendar never says when — so the
    /// detail finds out, and the analysis CTA becomes available because of it.
    /// </summary>
    [Fact]
    public async Task Split_times_that_exist_show_up_in_the_schedule()
    {
        var competition = await _source.GetCompetitionAsync(new CompetitionId("38499"));

        Assert.NotNull(competition!.Schedule.ResultsPublishedAt);
        Assert.Equal(competition.Schedule.ResultsPublishedAt, competition.Schedule.SplitsPublishedAt);
    }

    /// <summary>
    /// The calendar only knows the day. Once the start list is out, the first start is the
    /// earliest start in it — and the arena has to close after it, not twelve hours before.
    /// </summary>
    [Fact]
    public async Task The_first_start_comes_from_the_start_list()
    {
        var competition = await _source.GetCompetitionAsync(new CompetitionId("38412"));

        Assert.NotNull(competition);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 10, 4, 0, TimeSpan.FromHours(2)), competition.FirstStart);
        Assert.True(competition.LastFinish > competition.FirstStart);
    }

    [Fact]
    public async Task Results_and_starts_come_back_for_the_competition_they_belong_to()
    {
        var starts = await _source.GetStartsAsync(new CompetitionId("38412"));
        var results = await _source.GetResultsAsync(new CompetitionId("38499"));

        Assert.Equal(3, starts.Count);
        Assert.All(starts, s => Assert.Equal("38412", s.Competition.Value));

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.Equal("38499", r.Competition.Value));
        Assert.Contains(results, r => r.Splits.Count == 3);
    }

    [Fact]
    public async Task Split_times_are_asked_for_when_results_are()
    {
        await _source.GetResultsAsync(new CompetitionId("38499"));

        var query = _eventor.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/results/event")).RequestUri!.Query;

        Assert.Contains("includeSplitTimes=true", query);
    }

    [Fact]
    public async Task An_unreachable_Eventor_is_reported_as_unavailable()
    {
        _eventor.Fail = true;

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => _source.GetCompetitionsAsync());
    }

    [Fact]
    public async Task A_backend_without_a_key_never_calls_upstream()
    {
        var unconfigured = new EventorSource(
            new EventorClient(
                new HttpClient(_eventor) { BaseAddress = new Uri("https://eventor.example/api/") },
                Options.Create(new EventorOptions { ApiKey = string.Empty }),
                NullLogger<EventorClient>.Instance),
            new ResponseCache(new MemoryCache(new MemoryCacheOptions())),
            Options.Create(new EventorOptions { ApiKey = string.Empty }));

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => unconfigured.GetCompetitionsAsync());
        Assert.Empty(_eventor.Requests);
    }

    /// <summary>Eventor as the fixtures describe it, and a switch for when it is not there.</summary>
    private sealed class EventorStub : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public bool Fail { get; set; }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            if (Fail)
                throw new HttpRequestException("Eventor är nere.");

            var path = request.RequestUri!.AbsolutePath;

            string? fixture = path switch
            {
                var p when p.EndsWith("/events") => "events.xml",
                var p when p.EndsWith("/events/documents") => "documents.xml",
                var p when p.Contains("/event/") => "event.xml",
                var p when p.EndsWith("/eventclasses") => "eventclasses.xml",
                var p when p.EndsWith("/organisations") => "organisations.xml",
                var p when p.EndsWith("/starts/event") => "starts.xml",
                var p when p.EndsWith("/results/event") => "results.xml",
                _ => null,
            };

            if (fixture is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Fixture.Eventor(fixture).ToString(),
                    Encoding.UTF8,
                    "application/xml"),
            });
        }
    }
}
