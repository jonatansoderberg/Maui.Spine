using Orientera.Services.Notifications;

namespace Orientera.Tests;

/// <summary>
/// The route is the whole contract between the backend and the app's pages: the server says what a
/// notification is about, the app decides which page that is.
/// </summary>
public class PushRouteTests
{
    [Theory]
    [InlineData("competition/38412", PushRouteKind.Competition)]
    [InlineData("live/38412", PushRouteKind.Live)]
    [InlineData("results/38412", PushRouteKind.Results)]
    public void The_routes_the_backend_sends_are_understood(string route, PushRouteKind kind)
    {
        var parsed = PushRoute.Parse(route);

        Assert.Equal(kind, parsed?.Kind);
        Assert.Equal(new CompetitionId("38412"), parsed?.Competition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("results")]
    [InlineData("results/38412/extra")]
    [InlineData("ranking/38412")]
    public void Anything_else_is_no_route_rather_than_a_crash(string? route)
    {
        Assert.Null(PushRoute.Parse(route));
    }

    [Fact]
    public void A_route_round_trips_through_its_own_spelling()
    {
        var route = new PushRoute(PushRouteKind.Results, new CompetitionId("38412"));

        Assert.Equal("results/38412", route.ToString());
        Assert.Equal(route, PushRoute.Parse(route.ToString()));
    }
}

public class OnScreenTests
{
    private static readonly CompetitionId First = new("38412");
    private static readonly CompetitionId Second = new("38413");

    [Fact]
    public void Nothing_is_on_screen_to_begin_with()
    {
        Assert.Null(new OnScreen().Competition);
    }

    [Fact]
    public void The_page_that_appeared_is_the_one_on_screen()
    {
        var screen = new OnScreen();
        screen.Show(First);

        Assert.Equal(First, screen.Competition);
    }

    [Fact]
    public void A_page_leaving_does_not_clear_the_one_that_replaced_it()
    {
        // Appearing runs before disappearing when one page pushes another.
        var screen = new OnScreen();
        screen.Show(First);
        screen.Show(Second);
        screen.Hide(First);

        Assert.Equal(Second, screen.Competition);
    }

    [Fact]
    public void The_last_page_leaving_clears_the_screen()
    {
        var screen = new OnScreen();
        screen.Show(First);
        screen.Hide(First);

        Assert.Null(screen.Competition);
    }
}
