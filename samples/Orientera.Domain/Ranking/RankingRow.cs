
namespace Orientera.Domain.Ranking;

/// <summary>Which list a row belongs to. Only forest has a club-wise page today.</summary>
public enum RankingDiscipline
{
    Forest,
    Sprint,
}

/// <summary>
/// One runner's standing in Sverigelistan, as the club page states it.
/// </summary>
/// <remarks>
/// The club page links every runner to <c>/Ranking/ol/Runner/Index/{id}</c>, so a row carries a
/// real id and is not matched on a name. SP-02 said otherwise; it looked for <c>/Athlete/</c> and
/// <c>/Person/</c> and never for <c>/Runner/</c>, and concluded from not finding.
///
/// That id is the ranking's own, not Eventor's <c>personId</c>. It is stable enough to look a
/// runner up with, and it is what the app stores once the user has picked themselves out of their
/// club's list.
/// </remarks>
public sealed record RankingRow
{
    public required string ClubId { get; init; }

    /// <summary>Eventor's ranking id for this runner, from the club page's own link.</summary>
    public required string RunnerId { get; init; }
    public required string Name { get; init; }
    public required string Class { get; init; }

    /// <summary>Place within the club, as the page numbers it — from one, within the section.</summary>
    public required int ClubRank { get; init; }

    /// <summary>
    /// Which of the club page's two tables the row stood in. Null if the page stopped heading
    /// them, in which case the rank is still the page's own number and only its half is unknown.
    /// </summary>
    public RankingSection? Section { get; init; }

    /// <summary>Place on the national list. Absent for a runner the list does not rank.</summary>
    public int? NationalRank { get; init; }

    public required double Points { get; init; }

    public RankingDiscipline Discipline { get; init; } = RankingDiscipline.Forest;
}
