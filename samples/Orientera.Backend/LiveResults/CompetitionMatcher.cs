using Orientera.Domain;

namespace Orientera.Backend.LiveResults;

/// <summary>How well a LiveResults competition is believed to be the Eventor event.</summary>
public sealed record CompetitionMatch
{
    public required LiveCompetition Competition { get; init; }

    /// <summary>0–1, from the date, the organiser and the name.</summary>
    public required double Confidence { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// Spike SP-04: which LiveResults competition belongs to which Eventor event. The two systems
/// share no ids, so all there is to go on is the day, the organising club and the name.
/// </summary>
/// <remarks>
/// No match is a valid answer, and a deliberate one: showing another competition's live list
/// under this competition's name is worse than showing none, and the product already has the
/// deep-link fallback for when a source cannot be resolved.
/// </remarks>
public static class CompetitionMatcher
{
    /// <summary>Below this, the candidate is not offered at all.</summary>
    public const double MinimumConfidence = 0.6;

    public static CompetitionMatch? Match(Competition competition, IEnumerable<LiveCompetition> candidates)
    {
        var scored = candidates
            .Select(candidate => Score(competition, candidate))
            .OfType<CompetitionMatch>()
            .OrderByDescending(match => match.Confidence)
            .ToList();

        if (scored is not [var best, ..])
            return null;

        // Two candidates that score the same are two competitions we cannot tell apart —
        // a multi-race weekend where every race carries the same name is exactly that case.
        if (scored is [_, var runnerUp, ..] && best.Confidence - runnerUp.Confidence < 0.05)
            return null;

        return best.Confidence >= MinimumConfidence ? best : null;
    }

    private static CompetitionMatch? Score(Competition competition, LiveCompetition candidate)
    {
        // The date is not a signal, it is a precondition: a competition is run on its day.
        if (candidate.Date != competition.Date)
            return null;

        double name = Similarity(competition.Name, candidate.Name);
        double organiser = Similarity(competition.Organiser, candidate.Organizer);

        double confidence = (name * 0.6) + (organiser * 0.4);

        var reason = $"datum, arrangör {organiser:P0}, namn {name:P0}";

        return new CompetitionMatch
        {
            Competition = candidate,
            Confidence = Math.Round(confidence, 3),
            Reason = reason,
        };
    }

    /// <summary>
    /// Token overlap rather than edit distance: "Norrlandsmästerskapen, medel" and
    /// "Norrlandsmästerskapen medel" are the same competition written by two people, while
    /// "medel" and "lång" differ by one token that decides everything.
    /// </summary>
    private static double Similarity(string left, string right)
    {
        var a = Tokens(left);
        var b = Tokens(right);

        if (a.Count == 0 || b.Count == 0)
            return 0;

        int shared = a.Count(b.Contains);

        return (double)shared / Math.Max(a.Count, b.Count);
    }

    private static HashSet<string> Tokens(string value) =>
        [.. RunnerIdentity.Normalise(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}
