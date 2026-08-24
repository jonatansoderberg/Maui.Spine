using Orientera.Services.Context;

namespace Orientera.Tests;

public class ParticipantModeTests
{
    /// <summary>Nothing asked yet — the calendar is the only thing the engine has to go on.</summary>
    private static ParticipantInput Unasked(ContextState state, bool running = false) => new()
    {
        State = state,
        IsRunningNow = running,
    };

    private static ParticipantSightings Seen(params ParticipantMode[] modes)
    {
        var sightings = new ParticipantSightings();

        foreach (var mode in modes)
            sightings = sightings.Saw(mode, Sighting.Present);

        return sightings;
    }

    // ---------------------------------------------------------------- the ladder

    [Theory]
    [InlineData(ContextState.Discovered, ParticipantMode.Entries)]
    [InlineData(ContextState.RegistrationOpen, ParticipantMode.Entries)]
    [InlineData(ContextState.Registered, ParticipantMode.Entries)]
    [InlineData(ContextState.PmPublished, ParticipantMode.Entries)]
    [InlineData(ContextState.StartListPublished, ParticipantMode.StartList)]
    [InlineData(ContextState.RaceDay, ParticipantMode.StartList)]
    [InlineData(ContextState.Finished, ParticipantMode.Results)]
    [InlineData(ContextState.ResultsPublished, ParticipantMode.Results)]
    [InlineData(ContextState.SplitsAvailable, ParticipantMode.Results)]
    [InlineData(ContextState.MapAndAnalysisAvailable, ParticipantMode.Results)]
    public void Opens_on_the_mode_the_journey_has_reached(ContextState state, ParticipantMode expected)
    {
        var decision = ParticipantModeEngine.Decide(Unasked(state));

        Assert.Equal(expected, decision.Default);
    }

    [Fact]
    public void The_switcher_always_offers_all_four_in_lifecycle_order()
    {
        var decision = ParticipantModeEngine.Decide(Unasked(ContextState.Discovered));

        Assert.Equal(
            [ParticipantMode.Entries, ParticipantMode.StartList, ParticipantMode.Live, ParticipantMode.Results],
            decision.Modes.Select(offer => offer.Mode));
    }

    [Fact]
    public void A_mode_the_calendar_has_not_reached_says_when_it_will_exist()
    {
        var decision = ParticipantModeEngine.Decide(Unasked(ContextState.Registered));

        Assert.False(decision.IsAvailable(ParticipantMode.Live));
        Assert.Equal("finns när tävlingen startat", decision[ParticipantMode.Live].ConditionText);
    }

    [Fact]
    public void An_available_mode_carries_no_condition()
    {
        var decision = ParticipantModeEngine.Decide(Unasked(ContextState.ResultsPublished));

        Assert.True(decision.IsAvailable(ParticipantMode.Results));
        Assert.Empty(decision[ParticipantMode.Results].ConditionText);
    }

    // ---------------------------------------------------------------- live and its long tail

    [Fact]
    public void Live_is_the_default_while_anyone_is_out()
    {
        var decision = ParticipantModeEngine.Decide(Unasked(ContextState.Live, running: true));

        Assert.Equal(ParticipantMode.Live, decision.Default);
    }

    /// <summary>
    /// The arena stays open for hours after one class is done. The state is still Live, and the
    /// list the reader wants by then is the result list.
    /// </summary>
    [Fact]
    public void Live_gives_way_to_results_once_nobody_is_out()
    {
        var decision = ParticipantModeEngine.Decide(Unasked(ContextState.Live, running: false));

        Assert.Equal(ParticipantMode.Results, decision.Default);
    }

    /// <summary>
    /// Between the competition's first start and the class' own, nobody is out and nobody has
    /// finished. Results is preferred and has nothing behind it, so the page falls back down the
    /// ladder rather than opening on an empty list.
    /// </summary>
    [Fact]
    public void Before_the_class_sets_off_the_page_falls_back_to_the_start_list()
    {
        var decision = ParticipantModeEngine.Decide(new ParticipantInput
        {
            State = ContextState.Live,
            IsRunningNow = false,
            Sightings = new ParticipantSightings()
                .Saw(ParticipantMode.StartList, Sighting.Present)
                .Saw(ParticipantMode.Live, Sighting.Present)
                .Saw(ParticipantMode.Results, Sighting.Absent),
        });

        Assert.Equal(ParticipantMode.Live, decision.Default);
    }

    // ---------------------------------------------------------------- the answer beats the calendar

    /// <summary>D10: the calendar says the draw is out; the class' own list says otherwise.</summary>
    [Fact]
    public void A_source_that_answered_nothing_closes_a_mode_the_calendar_expected()
    {
        var decision = ParticipantModeEngine.Decide(new ParticipantInput
        {
            State = ContextState.StartListPublished,
            Sightings = new ParticipantSightings()
                .Saw(ParticipantMode.Entries, Sighting.Present)
                .Saw(ParticipantMode.StartList, Sighting.Absent),
        });

        Assert.False(decision.IsAvailable(ParticipantMode.StartList));
        Assert.Equal("klassen är inte lottad", decision[ParticipantMode.StartList].ConditionText);
        Assert.Equal(ParticipantMode.Entries, decision.Default);
    }

    /// <summary>The opposite direction: rows exist before the calendar admits they could.</summary>
    [Fact]
    public void A_source_that_answered_opens_a_mode_the_calendar_had_not_reached()
    {
        var decision = ParticipantModeEngine.Decide(new ParticipantInput
        {
            State = ContextState.Registered,
            Sightings = Seen(ParticipantMode.StartList),
        });

        Assert.True(decision.IsAvailable(ParticipantMode.StartList));
        Assert.Equal(ParticipantMode.StartList, decision.Default);
    }

    // ---------------------------------------------------------------- offline

    /// <summary>
    /// Offline is Unknown, and Unknown must never overwrite Present — otherwise the start list
    /// greys out for the runner standing at the arena reading it.
    /// </summary>
    [Fact]
    public void A_list_that_has_existed_survives_a_source_that_stops_answering()
    {
        var sightings = new ParticipantSightings()
            .Saw(ParticipantMode.StartList, Sighting.Present)
            .Saw(ParticipantMode.StartList, Sighting.Unknown);

        Assert.Equal(Sighting.Present, sightings.StartList);
    }

    /// <summary>An outright "nothing here" is not undone by a later outage either.</summary>
    [Fact]
    public void An_absence_survives_a_source_that_stops_answering()
    {
        var sightings = new ParticipantSightings()
            .Saw(ParticipantMode.Live, Sighting.Absent)
            .Saw(ParticipantMode.Live, Sighting.Unknown);

        Assert.Equal(Sighting.Absent, sightings.Live);
    }

    /// <summary>But an absence does give way to rows: a class can appear in the live list late.</summary>
    [Fact]
    public void An_absence_gives_way_to_rows()
    {
        var sightings = new ParticipantSightings()
            .Saw(ParticipantMode.Live, Sighting.Absent)
            .Saw(ParticipantMode.Live, Sighting.Present);

        Assert.Equal(Sighting.Present, sightings.Live);
    }

    [Fact]
    public void Sightings_belong_to_one_class_and_start_empty()
    {
        var sightings = new ParticipantSightings();

        Assert.All(
            Enum.GetValues<ParticipantMode>(),
            mode => Assert.Equal(Sighting.Unknown, sightings.For(mode)));
    }

    // ---------------------------------------------------------------- nothing at all

    /// <summary>
    /// Every source said no. The preference stands so the page has something to draw its empty
    /// state under, and every chip carries its own reason.
    /// </summary>
    [Fact]
    public void A_competition_with_nothing_behind_any_mode_keeps_its_preference()
    {
        var decision = ParticipantModeEngine.Decide(new ParticipantInput
        {
            State = ContextState.ResultsPublished,
            Sightings = new ParticipantSightings
            {
                Entries = Sighting.Absent,
                StartList = Sighting.Absent,
                Live = Sighting.Absent,
                Results = Sighting.Absent,
            },
        });

        Assert.Equal(ParticipantMode.Results, decision.Default);
        Assert.All(decision.Modes, offer => Assert.False(offer.IsAvailable));
        Assert.All(decision.Modes, offer => Assert.NotEmpty(offer.ConditionText));
    }
}
