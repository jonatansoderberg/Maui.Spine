using System.Net;
using System.Text;
using Orientera.Domain.Eventor;
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

/// <summary>
/// How long the app may claim the login lasts.
/// </summary>
/// <remarks>
/// Written after the promise "giltig till 16 sep 2027" appeared twice, off two different
/// advertising cookies. The jar here is the real one, captured on #123 with "kom ihåg mig"
/// ticked: twenty cookies, of which exactly one is Eventor's login and it has no expiry.
/// </remarks>
public class EventorSessionTests
{
    /// <summary>The jar as iOS handed it over, names and domains only — no values are real.</summary>
    private static EventorWebSession Session(params SessionCookie[] extra) => new()
    {
        Cookies =
        [
            new SessionCookie("ASP.NET_SessionId", "x", null) { Domain = "eventor.orientering.se" },
            .. extra,
        ],
        CapturedAt = DateTimeOffset.Now,
    };

    private static SessionCookie Tracker(string name, string domain, int year) =>
        new(name, "x", new DateTimeOffset(year, 9, 16, 11, 32, 27, TimeSpan.Zero)) { Domain = domain };

    [Fact]
    public void A_session_cookie_with_no_expiry_promises_nothing()
    {
        Assert.Null(Session().ExpiresAt);
    }

    [Fact]
    public void Googles_cookies_on_the_federations_domain_are_not_the_login()
    {
        // The four that brought the promise back the second time, all on .orientering.se.
        var session = Session(
            Tracker("_ga", ".orientering.se", 2027),
            Tracker("_ga_2775GT7RJT", ".orientering.se", 2027),
            Tracker("__gads", ".orientering.se", 2027),
            Tracker("__gpi", ".orientering.se", 2027),
            Tracker("__eoi", ".orientering.se", 2027));

        Assert.Null(session.ExpiresAt);
    }

    [Fact]
    public void Nor_are_the_advertising_cookies_on_Eventors_own_host()
    {
        var session = Session(
            Tracker("adkvid", "eventor.orientering.se", 2027),
            Tracker("ple", "eventor.orientering.se", 2027),
            Tracker("pld", "eventor.orientering.se", 2027),
            Tracker("__utma", ".eventor.orientering.se", 2027),
            Tracker("usprivacy", ".eventor.orientering.se", 2027));

        Assert.Null(session.ExpiresAt);
    }

    /// <summary>The day Eventor issues a dated login cookie, that date is the one to show.</summary>
    [Fact]
    public void A_dated_login_cookie_is_what_the_date_comes_from()
    {
        var expires = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var session = new EventorWebSession
        {
            Cookies =
            [
                new SessionCookie(EventorWebSession.LoginCookie, "x", expires),
                Tracker("_ga", ".orientering.se", 2030),
            ],
            CapturedAt = DateTimeOffset.Now,
        };

        Assert.Equal(expires, session.ExpiresAt);
    }
}

/// <summary>
/// "Mina tävlingar" — the page the entries are read off.
/// </summary>
/// <remarks>
/// The fixture is the real page for a real account: one season, thirty-nine rows, and exactly one
/// of them an entry that has not been run. That ratio is the point — the parser's job is almost
/// entirely to leave the other thirty-eight alone.
/// </remarks>
public class MyEventsPageParserTests
{
    private static readonly DateOnly Captured = new(2026, 8, 12);

    private static IReadOnlyList<EventorEntry> Entries(string fixture, DateOnly today) =>
        MyEventsPageParser.Parse(File.ReadAllText(Fixture.PathFor("Eventor", fixture)), today);

    [Fact]
    public void The_unrun_entry_is_found()
    {
        var entry = Assert.Single(Entries("myevents-121330.html", Captured));

        Assert.Equal("53725", entry.EventId);
        Assert.Equal("H21", entry.Class);
    }

    /// <summary>The count beside the class belongs to the competition and moves until the deadline.</summary>
    [Fact]
    public void The_number_of_entrants_is_not_part_of_the_class()
    {
        Assert.DoesNotContain(Entries("myevents-121330.html", Captured), e => e.Class.Contains('('));
    }

    /// <summary>
    /// Thirty-eight rows are races already run, and a season of results read as entries would put
    /// every race behind the runner back on the calendar as something to go to.
    /// </summary>
    [Fact]
    public void What_has_already_been_run_is_not_an_entry()
    {
        Assert.DoesNotContain(Entries("myevents-121330.html", Captured), e => e.EventId == "56311");
    }

    /// <summary>
    /// The same entry, two days later and past its deadline. Eventor has taken the "Ändra anmälan"
    /// link away and put "Starttid: 11:18" in its place — the runner is still entered, and the
    /// first version of this parser lost them the week of the race.
    /// </summary>
    [Fact]
    public void An_entry_survives_its_own_entry_deadline()
    {
        var entry = Assert.Single(Entries("myevents-121330-efter-anmalningstid.html", new DateOnly(2026, 8, 14)));

        Assert.Equal("53725", entry.EventId);
        Assert.Equal("H21", entry.Class);
    }

    /// <summary>A race being run today is still one the runner is entered in, until results exist.</summary>
    [Fact]
    public void A_competition_being_run_today_still_counts()
    {
        Assert.Single(Entries("myevents-121330-efter-anmalningstid.html", new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void Once_it_is_behind_the_runner_it_is_no_longer_an_entry()
    {
        Assert.Empty(Entries("myevents-121330-efter-anmalningstid.html", new DateOnly(2026, 8, 17)));
    }

    [Fact]
    public void A_page_without_a_table_is_no_entries_rather_than_a_throw()
    {
        Assert.Empty(MyEventsPageParser.Parse("<html><body>Ett fel uppstod</body></html>", Captured));
    }
}

public class EntryListPageParserTests
{
    private static IReadOnlyList<EventorEntrant> Entrants() =>
        EntryListPageParser.Parse(File.ReadAllText(Fixture.PathFor("Eventor", "entries-53725.html")));

    [Fact]
    public void Every_class_on_the_page_is_read()
    {
        Assert.True(Entrants().Select(e => e.Class).Distinct().Count() >= 20);
    }

    /// <summary>H21 says "(36)" in its heading, and the count must not leak into the class name.</summary>
    [Fact]
    public void A_class_holds_the_runners_its_heading_counts()
    {
        Assert.Equal(36, Entrants().Count(e => e.Class == "H21"));
    }

    [Fact]
    public void The_count_is_not_part_of_the_class_name()
    {
        Assert.DoesNotContain(Entrants(), e => e.Class.Contains('('));
    }

    [Fact]
    public void A_runner_carries_a_name_and_the_club_they_run_for()
    {
        var entrant = Assert.Single(Entrants(), e => e.Name == "Helena Backlund");

        Assert.Equal("Gävle OK", entrant.Club);
        Assert.Equal("D21", entrant.Class);
    }

    /// <summary>Before the draw there are no ids to match on, so the reader is found by name and club.</summary>
    [Fact]
    public void The_reader_can_be_found_in_the_list_without_an_id()
    {
        var me = RunnerIdentity.Of("Helena Backlund", "Gävle OK");

        Assert.Contains(Entrants(), e => RunnerIdentity.Of(e.Name, e.Club).Matches(me));
    }

    [Fact]
    public void A_page_with_no_classes_is_no_entrants_rather_than_a_throw()
    {
        Assert.Empty(EntryListPageParser.Parse("<html><body><p>Inga anmälda</p></body></html>"));
    }
}

/// <summary>
/// Telling a session Eventor has forgotten from an Eventor that is not answering.
/// </summary>
/// <remarks>
/// Measured on #123 two days after a live login: Eventor bounces a dead session between
/// <c>/Home/Index</c> and <c>/PersistentLogin</c> without end. Followed, that is an exception and
/// reads as an outage; unfollowed, the 302 is the answer — nobody is logged in.
/// </remarks>
public class ExpiredSessionTests
{
    private static EventorReader Reader(HttpMessageHandler handler)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orientera-session-{Guid.NewGuid():N}.json");
        var sessions = new EventorSessionStore(path);

        sessions.Save(new EventorWebSession
        {
            Cookies = [new SessionCookie("ASP.NET_SessionId", "dead", null)],
            PersonId = "121330",
            CapturedAt = DateTimeOffset.Now,
        });

        return new EventorReader(new HttpClient(handler), sessions);
    }

    [Fact]
    public async Task A_session_Eventor_has_forgotten_reads_as_expired_and_not_as_an_outage()
    {
        var reader = Reader(new RedirectingHandler());

        Assert.Equal(EventorAccess.Expired, await reader.AccessAsync());
    }

    [Fact]
    public async Task An_Eventor_that_is_not_answering_still_reads_as_unreachable()
    {
        var reader = Reader(new DeadHandler());

        Assert.Equal(EventorAccess.Unreachable, await reader.AccessAsync());
    }

    /// <summary>Eventor's own answer to a dead session: 302 to the persistent-login endpoint.</summary>
    private sealed class RedirectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://eventor.orientering.se/PersistentLogin");

            return Task.FromResult(response);
        }
    }

    private sealed class DeadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Eventor is not answering.");
    }
}

/// <summary>
/// The other half of "Mina tävlingar": the races already behind the runner.
/// </summary>
/// <remarks>
/// Eventor has no per-person result list anywhere else. A competition's result list knows its
/// field and the calendar knows the race, but only this page knows which of them were yours —
/// which is why the Resultat tab stood empty against real data until it was read.
/// </remarks>
public class MyResultsParserTests
{
    private static IReadOnlyList<EventorResult> Results(DateOnly today) =>
        MyEventsPageParser.ParseResults(
            File.ReadAllText(Fixture.PathFor("Eventor", "myevents-121330.html")), today);

    private static readonly DateOnly Captured = new(2026, 8, 12);

    [Fact]
    public void A_whole_season_of_results_is_read()
    {
        Assert.True(Results(Captured).Count >= 30);
    }

    [Fact]
    public void A_placement_carries_its_time_and_the_gap_to_the_winner()
    {
        var result = Results(Captured).First(r => r.EventId == "56311" && r.Place is not null);

        Assert.Equal(28, result.Place);
        Assert.Equal(new TimeSpan(1, 5, 51), result.Time);
        Assert.Equal(new TimeSpan(0, 26, 56), result.Behind);
        Assert.Equal("Karlstad Indoor , etapp 1", result.Name);
        Assert.Equal(new DateOnly(2026, 1, 5), result.Date);
    }

    /// <summary>
    /// "ej godkänd" is not a placement, and it is not nothing either — the runner started. The
    /// row is kept with no number rather than dropped or rounded to a zero.
    /// </summary>
    /// <summary>
    /// "ej godkänd" is not a placement, and it is not nothing either — the runner started and
    /// finished outside the classification. The row keeps Eventor's own word for it rather than
    /// being dropped or rounded to a zero.
    /// </summary>
    [Fact]
    public void A_race_that_was_not_classified_is_kept_with_Eventors_own_word_for_it()
    {
        var notClassified = Results(Captured).Where(r => r.PlaceText == "ej godkänd").ToList();

        Assert.NotEmpty(notClassified);
        Assert.All(notClassified, r => Assert.Null(r.Place));
    }

    /// <summary>An entry is not a result, however close its date is.</summary>
    [Fact]
    public void What_has_not_been_run_is_not_a_result()
    {
        Assert.DoesNotContain(Results(Captured), r => r.EventId == "53725");
    }

    [Fact]
    public void Two_hour_and_minute_formats_are_both_understood()
    {
        var results = Results(Captured).Where(r => r.Time is not null).ToList();

        Assert.Contains(results, r => r.Time > TimeSpan.FromHours(1));
        Assert.Contains(results, r => r.Time < TimeSpan.FromHours(1));
    }
}

/// <summary>
/// A multi-day event is one competition id and many races.
/// </summary>
/// <remarks>
/// Measured on the live page: O-Ringen's five stages all carry <c>eventId=50594</c>, run on five
/// different days, and Eventor tells them apart only in the row's own name — "etapp 3, medel".
/// Reading the id and asking the calendar what it was gave five identical rows on one date with
/// one discipline, two of which were the wrong distance.
/// </remarks>
public class MultiDayResultTests
{
    private static IReadOnlyList<EventorResult> Results() =>
        MyEventsPageParser.ParseResults(
            File.ReadAllText(Fixture.PathFor("Eventor", "myevents-121330-flerdagars.html")),
            new DateOnly(2026, 8, 17));

    [Fact]
    public void Every_stage_of_a_multi_day_event_is_its_own_result()
    {
        var stages = Results().Where(r => r.EventId == "50594").ToList();

        Assert.Equal(5, stages.Count);
    }

    /// <summary>The id repeats; the day does not, and the day is what tells the races apart.</summary>
    [Fact]
    public void The_stages_keep_their_own_days()
    {
        var days = Results().Where(r => r.EventId == "50594").Select(r => r.Date).ToList();

        Assert.Equal(days.Count, days.Distinct().Count());
    }

    [Fact]
    public void The_stages_keep_their_own_names()
    {
        var names = Results().Where(r => r.EventId == "50594").Select(r => r.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains(names, n => n.Contains("etapp 3"));
    }

    /// <summary>Two of the five were middle distance, which the container's "lång" had overwritten.</summary>
    [Fact]
    public void Eventor_states_the_distance_of_each_stage_in_its_name()
    {
        var stages = Results().Where(r => r.EventId == "50594").Select(r => r.Name).ToList();

        Assert.Equal(2, stages.Count(n => n.Contains("medel")));
        Assert.Equal(3, stages.Count(n => n.Contains("lång")));
    }
}

/// <summary>
/// The distance a race states in its own name.
/// </summary>
/// <remarks>
/// Needed because a multi-day event is one id and many distances, and because Eventor has no
/// classification at all for indoor — it calls those sprints. The name is the only source.
/// </remarks>
public class ResultDisciplineTests
{
    private static Discipline? Of(string name) =>
        MyEventsPageParser.ParseResults(Page(name), new DateOnly(2026, 8, 17)).Single().Discipline;

    private static string Page(string name) => $"""
        <table><tr>
          <td>2026-07-23</td><td><a href="/Events/Show/50594">{name}</a></td>
          <td>OK Tyr</td><td>H45 (12)</td><td>Gävle OK (3)</td><td>28</td><td>43:57</td><td>+5:00</td>
        </tr></table>
        """;

    [Theory]
    [InlineData("O-Ringen Göteborg, etapp 3, medel", Discipline.Middle)]
    [InlineData("O-Ringen Göteborg, etapp 5, lång", Discipline.Long)]
    [InlineData("DM, ultralång, Gästrikland + Hälsingland", Discipline.UltraLong)]
    [InlineData("Karlstad Indoor , etapp 1", Discipline.Indoor)]
    [InlineData("Vårsprinten", Discipline.Sprint)]
    [InlineData("DM, stafett, Gästrikland", Discipline.Relay)]
    [InlineData("Nattcupen, deltävling 2", Discipline.Night)]
    public void The_name_states_the_distance(string name, Discipline expected)
    {
        Assert.Equal(expected, Of(name));
    }

    /// <summary>"ultralång" contains "lång", and the longer word has to win.</summary>
    [Fact]
    public void Ultralong_is_not_read_as_long()
    {
        Assert.Equal(Discipline.UltraLong, Of("DM, ultralång"));
    }

    /// <summary>Indoor is a sprint by Eventor's classification and a different sport to a runner.</summary>
    [Fact]
    public void Indoor_wins_over_the_sprint_it_is_classified_as()
    {
        Assert.Equal(Discipline.Indoor, Of("Hallsberg Indoor sprint, dag 1"));
    }

    [Fact]
    public void A_name_that_says_nothing_leaves_it_to_the_calendar()
    {
        Assert.Null(Of("Valbos nationella"));
    }
}
