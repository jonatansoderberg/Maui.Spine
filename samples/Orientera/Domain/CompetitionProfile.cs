namespace Orientera.Domain;

public enum ProfileGroup
{
    Logistics,
    Competition,
    Terrain,
    ClassSpecific,
    Risk,
}

/// <summary>
/// One fact extracted from a PM or invitation. The LLM interprets the document, the domain
/// stores the structure: value plus where it came from, so the UI can always answer
/// "Måttligt kuperat — enligt vem?".
/// </summary>
public sealed record ProfileFact
{
    public required ProfileGroup Group { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }

    /// <summary>0–1. Below ~0.6 the UI should hedge the wording.</summary>
    public required double Confidence { get; init; }

    public required string SourceDocument { get; init; }
    public required int Page { get; init; }

    /// <summary>Classes the fact applies to. Empty means it applies to everyone.</summary>
    public IReadOnlyList<string> Classes { get; init; } = [];

    public string SourceLabel => $"{SourceDocument} sida {Page}";
}

/// <summary>
/// The structured reading of a competition's documents. Feeds both the briefing UI and the
/// prediction engine — a note that "ungdomsbanorna går i stigrikt område" must move the
/// prediction for H14 without touching H45, hence <see cref="ProfileFact.Classes"/>.
/// </summary>
public sealed record CompetitionProfile
{
    public required IReadOnlyList<ProfileFact> Facts { get; init; }

    public IEnumerable<ProfileFact> ForClass(string className) =>
        Facts.Where(f => f.Classes.Count == 0 || f.Classes.Contains(className));

    public IEnumerable<ProfileFact> InGroup(ProfileGroup group) =>
        Facts.Where(f => f.Group == group);
}
