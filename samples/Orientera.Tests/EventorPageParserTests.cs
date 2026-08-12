using Orientera.Domain.Eventor;

namespace Orientera.Tests;

/// <summary>
/// The two pages the app reads about the reader themselves, against real markup from Eventor.
/// </summary>
/// <remarks>
/// Same guard as the ranking parsers: the expected values are read off the saved pages, so a
/// layout change fails here instead of quietly turning into an app that thinks nobody is logged in.
/// </remarks>
public class EventorPageParserTests
{
    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Eventor", name));

    private static EventorStartPage StartPage(string name = "home-121330.html") =>
        StartPageParser.Parse(Page(name));

    [Fact]
    public void The_start_page_names_whoever_asked_for_it()
    {
        var page = StartPage();

        Assert.True(page.IsLoggedIn);
        Assert.Equal("Jonatan Söderberg", page.Name);
        Assert.Equal("121330", page.PersonId);
    }

    /// <summary>The club is what the activity list is looked up by, so both halves matter.</summary>
    [Fact]
    public void The_start_page_carries_the_club_and_its_id()
    {
        var page = StartPage();

        Assert.Equal("Gävle OK", page.Club);
        Assert.Equal("115", page.ClubId);
    }

    /// <summary>
    /// Logged out the page says nothing about anybody, and that is the whole liveness check: the
    /// session cookie carries no expiry, so the page is asked instead of the clock.
    /// </summary>
    [Fact]
    public void Logged_out_there_is_nobody_on_the_start_page()
    {
        var page = StartPage("home-anonymous.html");

        Assert.False(page.IsLoggedIn);
        Assert.False(page.HasRanking);
        Assert.Null(page.PersonId);
    }

    /// <summary>
    /// A reader whose club has not paid the fee is logged in and has no ranking box. Both empty
    /// cases pass through here, and they must not read as the same one.
    /// </summary>
    [Fact]
    public void A_club_without_sverigelistan_is_still_a_login()
    {
        var page = StartPageParser.Parse(
            Page("home-121330.html").Replace("/Ranking/ol/Runner/Index/121330", "/Ranking/ol/Index"));

        Assert.True(page.IsLoggedIn);
        Assert.False(page.HasRanking);
        Assert.Equal("Gävle OK", page.Club);
    }

    [Fact]
    public void Settings_carry_the_default_club()
    {
        var settings = SettingsPageParser.Parse(Page("settings-121330.html"));

        Assert.Equal("115", settings.ClubId);
        Assert.Equal("Gävle OK", settings.Club);
    }

    /// <summary>
    /// "Förvald klass 1" is H21 on this account while Sverigelistan ranks it in H45. The entered
    /// class is the one that finds the runner in a start list, so it is the one read.
    /// </summary>
    [Fact]
    public void Settings_carry_the_class_the_runner_enters()
    {
        var settings = SettingsPageParser.Parse(Page("settings-121330.html"));

        Assert.Equal("H21", settings.DefaultClass);
    }

    [Fact]
    public void A_page_that_is_not_the_expected_one_yields_nothing()
    {
        var page = StartPageParser.Parse("<html><body><p>Sidan finns inte</p></body></html>");
        var settings = SettingsPageParser.Parse("<html><body><p>Sidan finns inte</p></body></html>");

        Assert.False(page.IsLoggedIn);
        Assert.Null(settings.ClubId);
        Assert.Null(settings.DefaultClass);
    }
}
