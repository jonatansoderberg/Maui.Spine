using Orientera.Services.Context;

namespace Orientera.Tests;

public class ContextEngineTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour = 12, int minute = 0) =>
        new(new DateTime(year, month, day, hour, minute, 0), TimeSpan.FromHours(2));

    /// <summary>A competition with the full publication chain, mirroring the seeded NM Lång.</summary>
    private static Competition Championship() => new()
    {
        Id = new CompetitionId("c-test"),
        Name = "Testmästerskapen",
        Organiser = "Testklubben",
        District = "Gästrikland",
        Place = "Testarenan",
        Location = new GeoPoint(60.6, 17.1),
        Discipline = Discipline.Long,
        Level = CompetitionLevel.Championship,
        FirstStart = At(2026, 8, 15, 10, 0),
        LastFinish = At(2026, 8, 15, 13, 30),
        Schedule = new CompetitionSchedule
        {
            RegistrationOpensAt = At(2026, 6, 15, 0, 0),
            EntryDeadline = At(2026, 8, 9, 23, 59),
            PmPublishedAt = At(2026, 8, 8, 18, 0),
            StartListPublishedAt = At(2026, 8, 13, 20, 0),
            ResultsPublishedAt = At(2026, 8, 15, 14, 0),
            SplitsPublishedAt = At(2026, 8, 15, 16, 0),
            MapPublishedAt = At(2026, 8, 16, 10, 0),
        },
    };

    private static ContextInput Registered(DateTimeOffset now, Competition? competition = null) => new()
    {
        Now = now,
        Competition = competition ?? Championship(),
        MyEntryRegisteredAt = At(2026, 8, 5, 20, 12),
        MyStartTime = At(2026, 8, 15, 11, 4),
    };

    [Theory]
    [InlineData(2026, 6, 1, ContextState.Discovered)]
    [InlineData(2026, 6, 20, ContextState.RegistrationOpen)]
    [InlineData(2026, 8, 6, ContextState.Registered)]
    [InlineData(2026, 8, 9, ContextState.PmPublished)]
    [InlineData(2026, 8, 14, ContextState.StartListPublished)]
    public void Walks_the_lifecycle_forwards_as_the_clock_moves(int y, int m, int d, ContextState expected)
    {
        var decision = ContextEngine.Evaluate(Registered(At(y, m, d)));

        Assert.Equal(expected, decision.State);
    }

    [Theory]
    [InlineData(8, 0, ContextState.RaceDay)]
    [InlineData(11, 50, ContextState.Live)]
    [InlineData(13, 45, ContextState.Finished)]
    [InlineData(14, 30, ContextState.ResultsPublished)]
    [InlineData(16, 30, ContextState.SplitsAvailable)]
    public void Race_day_progresses_through_live_to_splits(int hour, int minute, ContextState expected)
    {
        var decision = ContextEngine.Evaluate(Registered(At(2026, 8, 15, hour, minute)));

        Assert.Equal(expected, decision.State);
    }

    [Fact]
    public void Map_publication_is_the_final_state()
    {
        var decision = ContextEngine.Evaluate(Registered(At(2026, 8, 16, 11, 0)));

        Assert.Equal(ContextState.MapAndAnalysisAvailable, decision.State);
        Assert.Equal(ContextAction.ShowRouteChoice, decision.PrimaryAction);
        Assert.Equal("Visa vägval", decision.PrimaryActionText);
    }

    /// <summary>One instant per context state, in lifecycle order.</summary>
    private static readonly DateTimeOffset[] LifecycleInstants =
    [
        At(2026, 6, 1), At(2026, 6, 20), At(2026, 8, 6), At(2026, 8, 9), At(2026, 8, 14),
        At(2026, 8, 15, 8, 0), At(2026, 8, 15, 11, 50), At(2026, 8, 15, 13, 45),
        At(2026, 8, 15, 14, 30), At(2026, 8, 15, 16, 30), At(2026, 8, 16, 11, 0),
    ];

    [Fact]
    public void Every_state_is_reachable_for_a_registered_runner()
    {
        var competition = Championship();

        var reached = LifecycleInstants
            .Select(now => ContextEngine.Evaluate(Registered(now, competition)).State)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<ContextState>().ToHashSet(), reached);
    }

    [Fact]
    public void Every_state_carries_its_own_labelled_action()
    {
        var competition = Championship();

        var decisions = LifecycleInstants
            .Select(now => ContextEngine.Evaluate(Registered(now, competition)))
            .ToList();

        Assert.Equal(decisions.Count, decisions.Select(d => d.PrimaryAction).Distinct().Count());
        Assert.All(decisions, d => Assert.False(string.IsNullOrWhiteSpace(d.PrimaryActionText)));
    }

    [Fact]
    public void An_unregistered_runner_stays_in_registration_even_after_the_pm_is_out()
    {
        var input = new ContextInput { Now = At(2026, 8, 9, 12, 0), Competition = Championship() };

        var decision = ContextEngine.Evaluate(input);

        Assert.Equal(ContextState.RegistrationOpen, decision.State);
        Assert.Equal("Anmäl dig", decision.PrimaryActionText);
    }

    [Fact]
    public void Registration_closes_at_the_deadline()
    {
        var input = new ContextInput { Now = At(2026, 8, 10, 12, 0), Competition = Championship() };

        Assert.Equal(ContextState.Discovered, ContextEngine.Evaluate(input).State);
    }

    [Fact]
    public void A_group_entry_alone_makes_the_competition_personal()
    {
        var input = new ContextInput
        {
            Now = At(2026, 8, 6),
            Competition = Championship(),
            GroupEntryRegisteredAt = At(2026, 8, 5, 20, 14),
        };

        var decision = ContextEngine.Evaluate(input);

        Assert.Equal(ContextState.Registered, decision.State);
        Assert.Equal("Förbered", decision.PrimaryActionText);
    }

    [Fact]
    public void An_entry_made_later_is_invisible_to_an_earlier_now()
    {
        var input = Registered(At(2026, 8, 4)) with { };

        // The entry is dated 5 August; rewinding past it must undo "Anmäld".
        Assert.Equal(ContextState.RegistrationOpen, ContextEngine.Evaluate(input).State);
    }

    [Fact]
    public void A_start_list_without_my_start_does_not_advance_the_state()
    {
        var input = new ContextInput
        {
            Now = At(2026, 8, 14),
            Competition = Championship(),
            MyEntryRegisteredAt = At(2026, 8, 5, 20, 12),
            MyStartTime = null,
        };

        Assert.Equal(ContextState.PmPublished, ContextEngine.Evaluate(input).State);
    }

    [Fact]
    public void Finished_precedes_published_results()
    {
        var input = Registered(At(2026, 8, 15, 13, 40));

        var decision = ContextEngine.Evaluate(input);

        Assert.Equal(ContextState.Finished, decision.State);
        Assert.Equal("Se preliminärt", decision.PrimaryActionText);
    }

    [Fact]
    public void A_competition_that_never_publishes_anything_stops_at_finished()
    {
        var bare = Championship() with { Schedule = new CompetitionSchedule() };
        var input = new ContextInput { Now = At(2026, 9, 1), Competition = bare };

        Assert.Equal(ContextState.Finished, ContextEngine.Evaluate(input).State);
    }
}
