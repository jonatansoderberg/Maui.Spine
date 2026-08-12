using System.Net;
using System.Text;
using Orientera.Services.Eventor;

namespace Orientera.Tests;

/// <summary>
/// The reading the app does on the phone, against the saved pages it will meet.
/// </summary>
/// <remarks>
/// The point of these is the empty cases. Sverigelistan behind a fee and a session with no
/// expiry means the app is wrong about what it can read more often than it is right, and the
/// three ways of being wrong — nobody logged in, a session Eventor has forgotten, a club that has
/// not paid — must not collapse into one shrug.
/// </remarks>
public class EventorReaderTests
{
    private static EventorReader Reader(bool loggedIn = true, string home = "home-121330.html")
    {
        var path = Path.Combine(Path.GetTempPath(), $"orientera-session-{Guid.NewGuid():N}.json");
        var sessions = new EventorSessionStore(path);

        if (loggedIn)
        {
            sessions.Save(new EventorWebSession
            {
                Cookies = [new SessionCookie("ASP.NET_SessionId", "abc123", null)],
                PersonId = "121330",
                CapturedAt = DateTimeOffset.Now,
            });
        }

        return new EventorReader(new HttpClient(new EventorHandler(home)), sessions);
    }

    [Fact]
    public async Task Without_a_login_there_is_nothing_to_read()
    {
        var reader = Reader(loggedIn: false);

        Assert.Equal(EventorAccess.NoSession, await reader.AccessAsync());
        Assert.Null(await reader.RankingAsync());
        Assert.Empty(await reader.ActivitiesAsync());
    }

    /// <summary>
    /// A session Eventor no longer knows gets the anonymous page back, with a 200 and no
    /// complaint. Only the missing greeting says so.
    /// </summary>
    [Fact]
    public async Task A_session_eventor_has_forgotten_reads_as_expired()
    {
        var reader = Reader(home: "home-anonymous.html");

        Assert.Equal(EventorAccess.Expired, await reader.AccessAsync());
        Assert.Null(await reader.RankingAsync());
    }

    /// <summary>
    /// The club without Sverigelistan. Logged in, no ranking box — and no invented placement,
    /// because "your club has not paid" is a different sentence from "we could not read it".
    /// </summary>
    [Fact]
    public async Task A_club_without_sverigelistan_is_told_apart_from_being_logged_out()
    {
        var reader = Reader(home: "home-no-ranking.html");

        Assert.Equal(EventorAccess.NoSubscription, await reader.AccessAsync());
        Assert.Null(await reader.RankingAsync());
        Assert.Empty(await reader.ClubRankingAsync(["115"]));
    }

    [Fact]
    public async Task With_a_login_the_runners_own_ranking_is_read()
    {
        var snapshot = await Reader().RankingAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(new PersonId("121330"), snapshot.Person);
        Assert.Equal(1914, snapshot.NationalPlace);
        Assert.Equal(62.98, snapshot.Points, 2);
        Assert.Equal("H45", snapshot.Class?.Class);
    }

    /// <summary>
    /// The club page is read for the place inside the club, and the saved one is another club's.
    /// A page without this runner's row leaves the place absent rather than guessing at it.
    /// </summary>
    [Fact]
    public async Task A_club_page_without_the_runners_row_leaves_the_place_out() =>
        Assert.Null((await Reader().RankingAsync())?.Club);

    [Fact]
    public async Task The_account_is_who_eventor_says_it_is()
    {
        var account = await Reader().ReadAccountAsync();

        Assert.NotNull(account);
        Assert.Equal("Jonatan Söderberg", account.Name);
        Assert.Equal("Gävle OK", account.Club);
        Assert.Equal("115", account.ClubId);

        // Eventor's "Förvald klass 1", which is what the runner enters — not the H45 the ranking
        // puts them in.
        Assert.Equal("H21", account.DefaultClass);
    }

    /// <summary>A start field is points from one club page per club, not one per runner.</summary>
    [Fact]
    public async Task A_start_fields_points_come_from_the_club_pages()
    {
        var rows = await Reader().ClubRankingAsync(["124", "124"]);

        Assert.Equal(35, rows.Count);
        Assert.Equal(3.30, rows["16695"].Points, 2);
    }

    [Fact]
    public async Task The_clubs_activities_are_read_for_the_club_the_reader_belongs_to()
    {
        var activities = await Reader().ActivitiesAsync();

        Assert.NotEmpty(activities);
        Assert.Contains(activities, a => a.Organisation == "Gävle OK");
    }

    /// <summary>
    /// Eventor being unreachable is not a statement about the session. Saying "log in again"
    /// because the train went into a tunnel would send the user to a login page for nothing.
    /// </summary>
    [Fact]
    public async Task An_unreachable_eventor_is_not_a_logged_out_one()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orientera-session-{Guid.NewGuid():N}.json");
        var sessions = new EventorSessionStore(path);

        sessions.Save(new EventorWebSession
        {
            Cookies = [new SessionCookie("ASP.NET_SessionId", "abc123", null)],
            PersonId = "121330",
            CapturedAt = DateTimeOffset.Now,
        });

        var reader = new EventorReader(new HttpClient(new UnreachableHandler()), sessions);

        Assert.Equal(EventorAccess.Unreachable, await reader.AccessAsync());
    }

    /// <summary>Eventor, as far as the reader is concerned: the saved pages, by path.</summary>
    private sealed class EventorHandler(string _home) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;

            string? file = path switch
            {
                "/Home/Index" => Fixture.PathFor("Eventor", _home),
                "/MyPages/Settings" => Fixture.PathFor("Eventor", "settings-121330.html"),
                "/Ranking/ol/Runner/Index/121330" => Fixture.PathFor("Ranking", "runner-121330.html"),
                _ when path.StartsWith("/Ranking/ol/Club/Index/") => Fixture.PathFor("Ranking", "club-124.html"),
                _ when path.StartsWith("/Activities?organisationId=115") => Fixture.PathFor("Activities", "activities-115.html"),
                _ => null,
            };

            return Task.FromResult(file is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText(file), Encoding.UTF8, "text/html"),
                });
        }
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Eventor is not answering.");
    }
}
