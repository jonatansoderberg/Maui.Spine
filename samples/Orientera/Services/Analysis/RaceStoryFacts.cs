using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Services.Analysis;

/// <summary>
/// What is worth saying about a race, picked out of the leg analysis.
/// </summary>
/// <remarks>
/// The facts are chosen here, deterministically, and only then handed to a language model to be
/// phrased. That order is the whole point: a coach who says "du var bland de snabbaste mellan
/// sjuan och tian" when the legs were middling is worse than no coach, and a model asked to both
/// find and phrase the story can do exactly that. Everything in <see cref="Lines"/> is computed
/// from <see cref="LegAnalysis"/> — the model is never asked to remember a number.
/// </remarks>
public sealed record RaceStoryFacts
{
    /// <summary>A leg counts as strong when it is among this share of the class' best.</summary>
    private const double StrongLegShare = 0.25;

    /// <summary>Shorter runs than this are a good leg, not a stretch worth naming.</summary>
    private const int ShortestStretch = 3;

    /// <summary>A podium's worth of leg times counts as strong even in a small class.</summary>
    private const int SmallestStrongGroup = 3;

    /// <summary>More than a couple of mistakes read as a list, not as a story.</summary>
    private const int MostMistakes = 2;

    public required string Class { get; init; }

    /// <summary>The facts, each one a finished statement the phrasing may not contradict.</summary>
    public required IReadOnlyList<string> Lines { get; init; }

    public static RaceStoryFacts From(
        CompetitionResult result,
        IReadOnlyList<LegAnalysis> legs,
        IReadOnlyList<CompetitionResult> field)
    {
        var lines = new List<string>(8);
        int starters = field.Count(r => r.Class == result.Class);

        if (legs.Count > 0)
            lines.Add(Start(legs[0]));

        if (Stretch(legs, starters) is { } stretch)
            lines.Add(stretch);

        foreach (var mistake in Mistakes(legs))
            lines.Add(mistake);

        if (Development(legs) is { } development)
            lines.Add(development);

        if (Steadiness(legs) is { } steadiness)
            lines.Add(steadiness);

        lines.Add(Finish(result, starters));

        return new RaceStoryFacts { Class = result.Class, Lines = lines };
    }

    private static string Start(LegAnalysis first) => first.LegPlace switch
    {
        1 => "Snabbast i klassen till första kontrollen.",
        <= 3 => $"{Format.Place(first.LegPlace)} snabbaste tid till första kontrollen.",
        _ => $"Till första kontrollen: {Format.Place(first.LegPlace)} sträcktid.",
    };

    /// <summary>
    /// The longest run of legs among the class' fastest. A single good leg is luck; three in a
    /// row is the part of the race worth taking with you.
    /// </summary>
    private static string? Stretch(IReadOnlyList<LegAnalysis> legs, int starters)
    {
        int strong = StrongLegPlace(starters);
        int best = 0, bestEnd = -1, run = 0;

        for (int i = 0; i < legs.Count; i++)
        {
            run = legs[i].LegPlace <= strong ? run + 1 : 0;

            if (run > best)
            {
                best = run;
                bestEnd = i;
            }
        }

        if (best < ShortestStretch)
            return null;

        var stretch = legs.Skip(bestEnd - best + 1).Take(best).ToList();
        string range = Range(stretch[0].ControlNumber, stretch[^1].ControlNumber);

        if (stretch.All(l => l.LegPlace == 1))
            return $"{range}: snabbaste sträcktid i klassen hela vägen.";

        return $"{range}: bland klassens snabbaste hela vägen "
             + $"(i snitt plats {stretch.Average(l => l.LegPlace):0.#} per sträcka).";
    }

    /// <summary>
    /// A run of legs, named by the controls it runs between — which is how a runner says it.
    /// </summary>
    /// <remarks>
    /// Leg <c>n</c> is the leg <em>into</em> control <c>n</c>, so legs 7–10 are the way from
    /// control 6 to control 10, not from 7. The first leg starts at the start, which has no
    /// number to give it.
    /// </remarks>
    private static string Range(int firstLeg, int lastLeg) => firstLeg <= 1
        ? $"Från start till kontroll {lastLeg}"
        : $"Från kontroll {firstLeg - 1} till {lastLeg}";

    /// <summary>
    /// The worst leg place that still counts as strong. A quarter of the class, but never more
    /// than half of it: in a class of four, "bland de snabbaste" has to mean something narrower
    /// than the top three, or the sentence is true of almost everyone who started.
    /// </summary>
    private static int StrongLegPlace(int starters) => Math.Min(
        Math.Max(SmallestStrongGroup, (int)Math.Ceiling(starters * StrongLegShare)),
        Math.Max(1, starters / 2));

    /// <summary>The mistakes that cost the most, and only those — modelled, so said as modelled.</summary>
    /// <remarks>
    /// Named by the control's order on the course, not by its code. The code is what the runner
    /// reads off the flag in the forest; afterwards it identifies nothing they remember.
    /// </remarks>
    private static IEnumerable<string> Mistakes(IReadOnlyList<LegAnalysis> legs) =>
        legs.Where(l => l.IsLikelyMistake)
            .OrderByDescending(l => l.EstimatedMistakeTime)
            .Take(MostMistakes)
            .Select(l => $"Kontroll {l.ControlNumber}: "
                       + $"uppskattat tapp omkring {Format.Time(l.EstimatedMistakeTime)} mot din egen fart.");

    private static string? Development(IReadOnlyList<LegAnalysis> legs)
    {
        if (legs.Count < 2)
            return null;

        int from = legs[0].PositionAfter;
        int to = legs[^1].PositionAfter;

        return (to - from) switch
        {
            <= -2 => $"Gick från {Format.Place(from)} efter första kontrollen till {Format.Place(to)} i mål.",
            >= 2 => $"Låg {Format.Place(from)} efter första kontrollen och {Format.Place(to)} i mål.",
            _ => null,
        };
    }

    /// <summary>
    /// Stability is the spread of the runner's own leg ratios. It says something only at the ends
    /// of the scale; in between it is a number without a message.
    /// </summary>
    private static string? Steadiness(IReadOnlyList<LegAnalysis> legs)
    {
        if (legs.Count < ShortestStretch)
            return null;

        double stability = SplitAnalyzer.StabilityIndex(legs);

        return stability switch
        {
            >= 0.9 => "Jämn fart genom hela loppet.",
            <= 0.7 => "Ojämnt lopp — farten svängde mycket mellan sträckorna.",
            _ => null,
        };
    }

    private static string Finish(CompetitionResult result, int starters)
    {
        if (result.Status != ResultStatus.Ok)
            return $"Loppet slutade med {Format.ResultStatus(result.Status).ToLowerInvariant()}.";

        string place = result.Place is { } p ? $"{Format.Place(p)} av {starters}" : $"i mål av {starters}";
        string behind = result.BehindWinner is { Ticks: > 0 } b ? $", {Format.Delta(b)} efter vinnaren" : string.Empty;

        return $"I mål: {place} på {Format.Time(result.Time)}{behind}.";
    }
}
