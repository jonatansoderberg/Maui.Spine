using Orientera.Backend.Eventor;

namespace Orientera.Tests;

/// <summary>
/// The adapter is where an external system stops being external. These pin what a competition
/// means once it has crossed that line — every assumption about Eventor's XML that the rest of
/// the product is allowed to forget.
/// </summary>
public class EventorNormalizerTests
{
    private readonly EventorNormalizer _normalizer = EventorNormalizer.ForZone("Europe/Stockholm");
    private readonly OrganisationDirectory _organisations =
        OrganisationDirectory.From(Fixture.Eventor("organisations.xml"));

    private IReadOnlyList<Competition> Competitions() =>
        _normalizer.Competitions(Fixture.Eventor("events.xml"), _organisations);

    private Competition Sprint() => Competitions().Single(c => c.Id.Value == "38412");

    [Fact]
    public void A_calendar_becomes_competitions_in_date_order()
    {
        var competitions = Competitions();

        Assert.Equal(7, competitions.Count);
        Assert.Equal(
            [
                "Norrlandsmästerskapen, medel",
                "Norrlandsmästerskapen, distriktsstafett",
                "DM, Sprint",
                "Veckans bana, etapp 6",
                "Veckans bana, etapp 7",
                "Veckans bana, etapp 8",
                "Natt-SM, långdistans",
            ],
            competitions.Select(c => c.Name));
    }

    [Fact]
    public void The_organiser_carries_its_club_and_its_district()
    {
        var sprint = Sprint();

        Assert.Equal("Gävle OK", sprint.Organiser);
        Assert.Equal("Gästrikland", sprint.District);
    }

    /// <summary>The organiser is sometimes a nested organisation, sometimes a bare id.</summary>
    [Fact]
    public void Both_shapes_of_organiser_resolve()
    {
        var competitions = Competitions();

        Assert.Equal("Gävle OK", competitions.Single(c => c.Id.Value == "38412").Organiser);
        Assert.Equal("Sandvikens OK", competitions.Single(c => c.Id.Value == "38499").Organiser);
    }

    [Fact]
    public void Times_are_read_in_the_federations_zone()
    {
        var sprint = Sprint();

        Assert.Equal(new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.FromHours(2)), sprint.FirstStart);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 15, 0, 0, TimeSpan.FromHours(2)), sprint.LastFinish);
    }

    /// <summary>A date without a clock says nothing about when the arena closes.</summary>
    [Fact]
    public void A_missing_finish_time_becomes_a_race_day_length()
    {
        var training = Competitions().Single(c => c.Id.Value == "38520");

        Assert.Equal(training.FirstStart.AddHours(6), training.LastFinish);
    }

    [Fact]
    public void The_arena_position_is_read_longitude_first()
    {
        var sprint = Sprint();

        Assert.Equal(60.6749, sprint.Location.Latitude, precision: 4);
        Assert.Equal(17.1413, sprint.Location.Longitude, precision: 4);
    }

    /// <summary>A relay is a relay whatever its legs measure — the event form decides.</summary>
    [Theory]
    [InlineData("38412", Discipline.Sprint)]
    [InlineData("38499", Discipline.Night)]
    [InlineData("38520", Discipline.Middle)]
    [InlineData("38601", Discipline.Relay)]
    public void The_discipline_follows_distance_and_light(string id, Discipline expected) =>
        Assert.Equal(expected, Competitions().Single(c => c.Id.Value == id).Discipline);

    [Theory]
    [InlineData("38412", CompetitionLevel.District)]
    [InlineData("38499", CompetitionLevel.Championship)]
    [InlineData("38520", CompetitionLevel.Training)]
    public void The_classification_becomes_a_level(string id, CompetitionLevel expected) =>
        Assert.Equal(expected, Competitions().Single(c => c.Id.Value == id).Level);

    /// <summary>"Dölj träningar" has to hide the club events — that is what it is for.</summary>
    [Fact]
    public void A_club_event_is_low_priority()
    {
        var training = Competitions().Single(c => c.Id.Value == "38520");

        Assert.True(training.IsLowPriority);
        Assert.False(Competitions().Single(c => c.Id.Value == "38499").IsLowPriority);
    }

    /// <summary>Ordinary entry closes first; the later break is late entry, at a price.</summary>
    [Fact]
    public void The_first_entry_period_carries_the_deadline()
    {
        var nightChampionship = Competitions().Single(c => c.Id.Value == "38499");

        Assert.Equal(
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.FromHours(2)),
            nightChampionship.Schedule.EntryDeadline);
    }

    /// <summary>
    /// An entry break is the period entry is <em>open</em>: from when it opens, to when it
    /// closes. Reading the wrong end put the deadline months early.
    /// </summary>
    [Fact]
    public void An_entry_break_is_the_period_entry_is_open()
    {
        var sprint = Sprint();

        Assert.Equal(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            sprint.Schedule.RegistrationOpensAt);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 10, 23, 59, 59, TimeSpan.FromHours(2)),
            sprint.Schedule.EntryDeadline);

        var decision = Services.Context.ContextEngine.Evaluate(new ContextInput
        {
            Competition = sprint,
            Now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.FromHours(2)),
        });

        Assert.Equal(ContextState.RegistrationOpen, decision.State);
    }

    /// <summary>
    /// Eventor records publication as a hash table entry keyed by the race, with an exact
    /// timestamp — not as an attribute on the event, which is what M1 assumed.
    /// </summary>
    [Fact]
    public void Publication_times_come_from_the_hash_table()
    {
        var sprint = Sprint();

        Assert.Equal(
            new DateTimeOffset(2026, 8, 13, 19, 40, 12, TimeSpan.FromHours(2)),
            sprint.Schedule.StartListPublishedAt);

        Assert.Null(sprint.Schedule.ResultsPublishedAt);

        var championship = Competitions().Single(c => c.Id.Value == "38499");

        Assert.Equal(
            new DateTimeOffset(2026, 9, 6, 9, 40, 0, TimeSpan.FromHours(2)),
            championship.Schedule.ResultsPublishedAt);
    }

    /// <summary>
    /// The Swedish instance lists foreign clubs too, and their competitions are not ours. The
    /// organisation's own country decides — no guessing from names.
    /// </summary>
    [Fact]
    public void A_competition_organised_abroad_is_left_out()
    {
        Assert.DoesNotContain(Competitions(), c => c.Id.Value == "38999");
        Assert.Contains(Competitions(), c => c.Organiser == "Gävle OK");
    }

    [Fact]
    public void A_published_start_list_is_visible_in_the_schedule()
    {
        var competitions = Competitions();

        Assert.NotNull(competitions.Single(c => c.Id.Value == "38412").Schedule.StartListPublishedAt);
        Assert.Null(competitions.Single(c => c.Id.Value == "38412").Schedule.ResultsPublishedAt);
        Assert.NotNull(competitions.Single(c => c.Id.Value == "38499").Schedule.ResultsPublishedAt);
        Assert.Null(competitions.Single(c => c.Id.Value == "38520").Schedule.StartListPublishedAt);
        Assert.Null(competitions.Single(c => c.Id.Value == "38520").Schedule.ResultsPublishedAt);
    }

    // ---------------------------------------------------------------- documents

    [Fact]
    public void Documents_are_kept_for_their_own_event_only()
    {
        var documents = _normalizer.Documents(Fixture.Eventor("documents.xml"), new CompetitionId("38412"));

        Assert.DoesNotContain(documents, d => d.Title == "PM Natt-SM");
    }

    [Fact]
    public void A_document_that_cannot_be_classified_is_left_out()
    {
        var documents = _normalizer.Documents(Fixture.Eventor("documents.xml"), new CompetitionId("38412"));

        Assert.Equal(
            [DocumentKind.Pm, DocumentKind.Invitation, DocumentKind.OldMap],
            documents.Select(d => d.Kind));

        Assert.DoesNotContain(documents, d => d.Title == "Anmälningsläge");
    }

    [Fact]
    public void A_document_keeps_when_it_was_published()
    {
        var pm = _normalizer
            .Documents(Fixture.Eventor("documents.xml"), new CompetitionId("38412"))
            .First(d => d.Kind == DocumentKind.Pm);

        Assert.Equal(new DateTimeOffset(2026, 8, 12, 21, 4, 0, TimeSpan.FromHours(2)), pm.PublishedAt);
    }

    [Fact]
    public void Classes_prefer_the_short_name_a_runner_recognises()
    {
        var classes = _normalizer.Classes(Fixture.Eventor("eventclasses.xml"));

        Assert.Equal(["H20", "H21", "D21", "Herrar 45 år"], classes);
    }

    // ---------------------------------------------------------------- starts

    [Fact]
    public void Starts_come_out_in_start_order_with_their_class()
    {
        var starts = _normalizer.Starts(Fixture.Eventor("starts.xml"), new CompetitionId("38412"));

        Assert.Equal(3, starts.Count);
        Assert.Equal(["H21", "D21", "H21"], starts.Select(s => s.Class));
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 10, 4, 0, TimeSpan.FromHours(2)), starts[0].StartTime);
        Assert.Equal(104, starts.Single(s => s.Person.Value == "144210").BibNumber);
    }

    /// <summary>A walk-up starter has no person id but still belongs in the start list.</summary>
    [Fact]
    public void A_starter_without_a_person_id_is_kept()
    {
        var starts = _normalizer.Starts(Fixture.Eventor("starts.xml"), new CompetitionId("38412"));

        Assert.Contains(starts, s => s.Person.Value.StartsWith("anon:", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- results

    private IReadOnlyList<CompetitionResult> Results() =>
        _normalizer.Results(Fixture.Eventor("results.xml"), new CompetitionId("38499"));

    [Fact]
    public void A_result_carries_what_a_result_list_shows()
    {
        var winner = Results().Single(r => r.Id.Value == "771001");

        Assert.Equal("Erik Lundqvist", winner.Name);
        Assert.Equal("Gävle OK", winner.Club);
        Assert.Equal("H21", winner.Class);
        Assert.Equal(new TimeSpan(1, 2, 33), winner.Time);
        Assert.Equal(1, winner.Place);
        Assert.Equal(TimeSpan.Zero, winner.BehindWinner);
        Assert.Equal(3, winner.Starters);
    }

    [Fact]
    public void Every_given_name_is_part_of_the_name()
    {
        var starts = _normalizer.Starts(Fixture.Eventor("starts.xml"), new CompetitionId("38412"));

        Assert.Equal("D21", starts.Single(s => s.Person.Value == "144233").Class);
        Assert.Equal("Anna Berg", Results().Single(r => r.Person.Value == "144233").Name);
    }

    [Theory]
    [InlineData("771001", ResultStatus.Ok)]
    [InlineData("771003", ResultStatus.Mispunch)]
    [InlineData("771004", ResultStatus.DidNotStart)]
    [InlineData("771010", ResultStatus.Preliminary)]
    public void Eventors_statuses_become_the_five_the_domain_shows(string id, ResultStatus expected) =>
        Assert.Equal(expected, Results().Single(r => r.Id.Value == id).Status);

    /// <summary>
    /// Eventor reports the elapsed time at a control; the leg time is the difference, and the
    /// whole split analysis rests on getting that right.
    /// </summary>
    [Fact]
    public void Split_times_are_elapsed_and_leg_both()
    {
        var splits = Results().Single(r => r.Id.Value == "771001").Splits;

        Assert.Equal(3, splits.Count);
        Assert.Equal(["62", "71", "34"], splits.Select(s => s.ControlCode));
        Assert.Equal([1, 2, 3], splits.Select(s => s.ControlNumber));

        Assert.Equal(new TimeSpan(0, 4, 12), splits[0].ElapsedTime);
        Assert.Equal(new TimeSpan(0, 4, 12), splits[0].LegTime);
        Assert.Equal(new TimeSpan(0, 11, 48), splits[1].ElapsedTime);
        Assert.Equal(new TimeSpan(0, 7, 36), splits[1].LegTime);
        Assert.Equal(new TimeSpan(0, 7, 14), splits[2].LegTime);
    }

    [Fact]
    public void A_result_without_split_times_simply_has_none() =>
        Assert.Empty(Results().Single(r => r.Id.Value == "771003").Splits);
}
