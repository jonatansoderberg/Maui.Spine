using Orientera.Domain;

namespace Orientera.Services.Relevance;

/// <summary>What the engine knows about the user when it scores a competition.</summary>
public sealed record RelevanceContext
{
    public required DateTimeOffset Now { get; init; }
    public required GeoPoint Home { get; init; }
    public required string HomeDistrict { get; init; }
    public required string MyClass { get; init; }

    public IReadOnlySet<CompetitionId> MyEntries { get; init; } = new HashSet<CompetitionId>();
    public IReadOnlySet<CompetitionId> GroupEntries { get; init; } = new HashSet<CompetitionId>();
    public IReadOnlySet<CompetitionId> Interests { get; init; } = new HashSet<CompetitionId>();
    public IReadOnlySet<SeriesId> FollowedSeries { get; init; } = new HashSet<SeriesId>();
    public IReadOnlySet<string> FollowedOrganisers { get; init; } = new HashSet<string>();

    /// <summary>
    /// The kinds of race the reader would rather be at, best first. Empty when they have not said.
    /// </summary>
    public IReadOnlyList<RacePreference> Favourites { get; init; } = [];
}

/// <summary>The six sub-scores, each 0–1, plus the weighted total.</summary>
public sealed record RelevanceScore
{
    public required double Importance { get; init; }
    public required double Personal { get; init; }
    public required double Geographic { get; init; }
    public required double Temporal { get; init; }
    public required double Urgency { get; init; }

    /// <summary>How near the top of the reader's own list this kind of race is.</summary>
    public required double Preference { get; init; }

    public double Total =>
        Importance * RelevanceEngine.ImportanceWeight
        + Personal * RelevanceEngine.PersonalWeight
        + Geographic * RelevanceEngine.GeographicWeight
        + Temporal * RelevanceEngine.TemporalWeight
        + Urgency * RelevanceEngine.UrgencyWeight
        + Preference * RelevanceEngine.PreferenceWeight;
}

/// <summary>
/// Relevance is its own component, not a sort inside a ViewModel. It weighs how big the
/// competition is, how personal it is, how far away it is and how soon it happens.
/// </summary>
/// <remarks>
/// The weights encode the spec's balance: personal signals dominate, but importance is
/// heavy enough that a championship 150 km away still outranks a nearby training event —
/// "nära events prioriteras, men får inte alltid slå mästerskap".
/// <para>
/// Urgency is separate from timeliness on purpose. How soon a race happens is a fact about the
/// calendar; how soon its entry closes is a fact about the reader, and it is the only one of the
/// five that expires. Measured on the Gästrikland calendar: a närtävling twelve kilometres away
/// whose entry closed in three days sat below a championship in another district eighty
/// kilometres away, because the deadline was worth at most 3.75 of a hundred points inside the
/// temporal axis. It is its own axis now.
/// </para>
/// </remarks>
public static class RelevanceEngine
{
    public const double PersonalWeight = 0.35;
    public const double GeographicWeight = 0.25;
    public const double ImportanceWeight = 0.20;
    public const double TemporalWeight = 0.10;
    public const double UrgencyWeight = 0.10;

    /// <summary>
    /// What the reader says they would rather run.
    /// </summary>
    /// <remarks>
    /// Added on top of the five rather than carved out of them. The weights above sum to one and
    /// the balance between them was argued for and measured; a sixth weight scales all five
    /// equally, which leaves that balance exactly as it was and gives taste a sixth of the total.
    /// Dividing the existing five to make room would have quietly undone the urgency fix.
    /// </remarks>
    public const double PreferenceWeight = 0.20;

    /// <summary>Distance at which the geographic score reaches zero.</summary>
    public const double MaxDistanceKm = 250.0;

    /// <summary>How far ahead a closing entry starts to feel like something to act on.</summary>
    /// <remarks>
    /// Two weeks, which is about when a runner starts planning a weekend. Beyond it the deadline
    /// is a date in the calendar, not a reason to open the app today.
    /// </remarks>
    public const double UrgencyHorizonDays = 14.0;

    public static RelevanceScore Score(Competition competition, RelevanceContext context) => new()
    {
        Importance = ImportanceScore(competition, context),
        Personal = PersonalScore(competition, context),
        Geographic = GeographicScore(competition, context),
        Temporal = TemporalScore(competition, context),
        Urgency = UrgencyScore(competition, context),
        Preference = PreferenceScore(competition, context),
    };

    /// <summary>
    /// Where this kind of race sits on the reader's own list, as a score.
    /// </summary>
    /// <remarks>
    /// The position is the weight — first place is worth twice second, which is worth half again
    /// as much as third. The curve does not depend on how long the list is: adding a sixth
    /// favourite must not make the first one matter less, or a runner who fills the list in
    /// carefully ends up with a flatter calendar than one who named a single race.
    /// <para>
    /// Nothing at all for a kind that is not on the list. It is a preference and not a filter —
    /// the race still appears, further down.
    /// </para>
    /// </remarks>
    public static double PreferenceScore(Competition competition, RelevanceContext context)
    {
        for (int i = 0; i < context.Favourites.Count; i++)
        {
            if (Matches(context.Favourites[i], competition))
                return 1.0 / (1.0 + i);
        }

        return 0.0;
    }

    /// <summary>
    /// A favourite with no distance is the sport itself, and matches every race in it — which is
    /// the only thing "Indoor" can mean, since indoor races have no distances to choose between.
    /// </summary>
    private static bool Matches(RacePreference favourite, Competition competition) =>
        favourite.Sport == competition.Sport
        && (favourite.Discipline is not { } distance || distance == competition.Discipline);

    /// <summary>
    /// How precisely relevance is worth believing.
    /// </summary>
    /// <remarks>
    /// Two decimals, because the inputs do not support more. Gästriklands DM medel and DM stafett
    /// are the same championship, arranged by the same club, at arenas forty metres apart — both
    /// shown as 41 km. The geographic score differed in the fifth decimal, that decided the order,
    /// and the list put Sunday's race above Saturday's. Rounding first lets the date settle what
    /// the score genuinely cannot.
    /// </remarks>
    public const int MeaningfulDigits = 2;

    /// <summary>The score the list orders by — rounded, so that noise cannot outvote the date.</summary>
    public static double Ranking(Competition competition, RelevanceContext context) =>
        Math.Round(Score(competition, context).Total, MeaningfulDigits);

    public static IReadOnlyList<Competition> Rank(
        IEnumerable<Competition> competitions,
        RelevanceContext context) =>
        competitions
            .Select(c => (Competition: c, Score: Ranking(c, context)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Competition.FirstStart)
            .Select(x => x.Competition)
            .ToList();

    public static double ImportanceScore(Competition competition, RelevanceContext context)
    {
        double baseScore = competition.Level switch
        {
            CompetitionLevel.International => 1.00,
            CompetitionLevel.Championship => 0.90,
            CompetitionLevel.National => 0.75,
            CompetitionLevel.District => 0.55,
            CompetitionLevel.Local => 0.35,
            CompetitionLevel.Training => 0.12,
            CompetitionLevel.Recreational => 0.08,
            _ => 0.30,
        };

        // A round in a series the user follows carries more than its level suggests.
        if (competition.Series is { } series && context.FollowedSeries.Contains(series))
            baseScore += 0.15;

        return Clamp(baseScore);
    }

    public static double PersonalScore(Competition competition, RelevanceContext context)
    {
        if (context.MyEntries.Contains(competition.Id))
            return 1.0;

        double score = 0.0;

        if (context.GroupEntries.Contains(competition.Id))
            score = Math.Max(score, 0.75);

        if (context.Interests.Contains(competition.Id))
            score = Math.Max(score, 0.60);

        if (context.FollowedOrganisers.Contains(competition.Organiser))
            score = Math.Max(score, 0.45);

        if (competition.Series is { } series && context.FollowedSeries.Contains(series))
            score = Math.Max(score, 0.45);

        // A competition without my class is not for me, however big it is.
        if (competition.Classes.Count > 0)
            score += competition.Classes.Contains(context.MyClass) ? 0.20 : -0.25;

        return Clamp(score);
    }

    public static double GeographicScore(Competition competition, RelevanceContext context)
    {
        // Distance is a guess at whether the reader would travel. For a race they have entered the
        // guess has been overtaken by a fact, and charging them for it is wrong: a club evening in
        // Dalarna they are signed up for scored zero here and fell out of the top of their own
        // calendar the moment geography was weighted up.
        if (context.MyEntries.Contains(competition.Id))
            return 1.0;

        // An arena with no published position scores nothing on distance. It is not near — we
        // cannot say that — and the district share below is what it still has to stand on.
        double byDistance = competition.DistanceFrom(context.Home) is { } distance
            ? Clamp(1.0 - (distance / MaxDistanceKm))
            : 0.0;

        // The chosen district takes a fixed share of the score rather than being added on top:
        // an additive boost would saturate every nearby competition at 1.0 and flatten the
        // distance ordering the list depends on.
        const double districtShare = 0.20;
        bool inDistrict = competition.District == context.HomeDistrict;

        return Clamp((byDistance * (1.0 - districtShare)) + (inDistrict ? districtShare : 0.0));
    }

    public static double TemporalScore(Competition competition, RelevanceContext context)
    {
        // A competition that is running right now is as timely as it gets — without this it
        // would fall into the decay branch below and rank under tomorrow's events.
        if (context.Now >= competition.FirstStart && context.Now < competition.LastFinish)
            return 1.0;

        double daysAway = (competition.FirstStart - context.Now).TotalDays;

        // The past decays fast — a week-old competition is nearly irrelevant in a calendar.
        if (daysAway < 0)
            return Clamp(1.0 + (daysAway / 7.0)) * 0.5;

        return daysAway switch
        {
            <= 1 => 1.00,
            <= 3 => 0.90,
            <= 7 => 0.75,
            <= 14 => 0.55,
            <= 30 => 0.35,
            <= 60 => 0.20,
            _ => 0.10,
        };
    }

    /// <summary>How much is left of the chance to enter at all.</summary>
    /// <remarks>
    /// The opening date only suppresses — it is not required. The old deadline bonus asked for
    /// both dates, and Eventor's calendar publishes an opening date for almost nothing, so the
    /// bonus never fired for the competitions it was written for.
    /// </remarks>
    public static double UrgencyScore(Competition competition, RelevanceContext context)
    {
        // Deliberately blind to whether the reader has already entered. Zeroing the axis for an
        // entry of your own reads as "nothing left to do", but it costs that competition ten
        // points against every stranger with an open deadline — and the one race you are actually
        // running fell out of the top of the list.
        if (competition.Schedule.EntryDeadline is not { } deadline)
            return 0.0;

        // Not open yet is not urgent — there is nothing to do about it today.
        if (competition.Schedule.RegistrationOpensAt is { } opens && opens > context.Now)
            return 0.0;

        double daysLeft = (deadline - context.Now).TotalDays;

        // A deadline that has passed is not urgent, it is over.
        if (daysLeft < 0)
            return 0.0;

        return Clamp(1.0 - (daysLeft / UrgencyHorizonDays));
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
