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
    public IReadOnlySet<CompetitionId> Favourites { get; init; } = new HashSet<CompetitionId>();
    public IReadOnlySet<SeriesId> FollowedSeries { get; init; } = new HashSet<SeriesId>();
    public IReadOnlySet<string> FollowedOrganisers { get; init; } = new HashSet<string>();
}

/// <summary>The four sub-scores, each 0–1, plus the weighted total.</summary>
public sealed record RelevanceScore
{
    public required double Importance { get; init; }
    public required double Personal { get; init; }
    public required double Geographic { get; init; }
    public required double Temporal { get; init; }

    public double Total =>
        Importance * RelevanceEngine.ImportanceWeight
        + Personal * RelevanceEngine.PersonalWeight
        + Geographic * RelevanceEngine.GeographicWeight
        + Temporal * RelevanceEngine.TemporalWeight;
}

/// <summary>
/// Relevance is its own component, not a sort inside a ViewModel. It weighs how big the
/// competition is, how personal it is, how far away it is and how soon it happens.
/// </summary>
/// <remarks>
/// The weights encode the spec's balance: personal signals dominate, but importance is
/// heavy enough that a championship 150 km away still outranks a nearby training event —
/// "nära events prioriteras, men får inte alltid slå mästerskap".
/// </remarks>
public static class RelevanceEngine
{
    public const double PersonalWeight = 0.40;
    public const double ImportanceWeight = 0.25;
    public const double GeographicWeight = 0.20;
    public const double TemporalWeight = 0.15;

    /// <summary>Distance at which the geographic score reaches zero.</summary>
    public const double MaxDistanceKm = 250.0;

    public static RelevanceScore Score(Competition competition, RelevanceContext context) => new()
    {
        Importance = ImportanceScore(competition, context),
        Personal = PersonalScore(competition, context),
        Geographic = GeographicScore(competition, context),
        Temporal = TemporalScore(competition, context),
    };

    public static IReadOnlyList<Competition> Rank(
        IEnumerable<Competition> competitions,
        RelevanceContext context) =>
        competitions
            .Select(c => (Competition: c, Score: Score(c, context).Total))
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

        if (context.Favourites.Contains(competition.Id))
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
        double distance = context.Home.DistanceKmTo(competition.Location);
        double byDistance = Clamp(1.0 - (distance / MaxDistanceKm));

        // The chosen district takes a fixed share of the score rather than being added on top:
        // an additive boost would saturate every nearby competition at 1.0 and flatten the
        // distance ordering the list depends on.
        const double districtShare = 0.15;
        bool inDistrict = competition.District == context.HomeDistrict;

        return Clamp((byDistance * (1.0 - districtShare)) + (inDistrict ? districtShare : 0.0));
    }

    public static double TemporalScore(Competition competition, RelevanceContext context)
    {
        double daysAway = (competition.FirstStart - context.Now).TotalDays;

        // The past decays fast — a week-old competition is nearly irrelevant in a calendar.
        if (daysAway < 0)
            return Clamp(1.0 + (daysAway / 7.0)) * 0.5;

        double score = daysAway switch
        {
            <= 1 => 1.00,
            <= 3 => 0.90,
            <= 7 => 0.75,
            <= 14 => 0.55,
            <= 30 => 0.35,
            <= 60 => 0.20,
            _ => 0.10,
        };

        // A closing entry deadline pulls a competition up the list.
        if (competition.Schedule is { RegistrationOpensAt: { } opens, EntryDeadline: { } deadline }
            && opens <= context.Now && context.Now <= deadline)
        {
            double daysLeft = (deadline - context.Now).TotalDays;
            if (daysLeft <= 7)
                score += 0.25 * (1.0 - (daysLeft / 7.0));
        }

        return Clamp(score);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
}
