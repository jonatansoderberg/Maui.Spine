namespace Orientera.Domain;

/// <summary>
/// One row in the competition list. A recurring series collapses into a single entry —
/// "Veckans bana – Hemlingby, 4–9 aug, 6 tillfällen" — while an ordinary competition is a
/// group of one. The originals stay reachable through <see cref="Occurrences"/>.
/// </summary>
public sealed record EventGroup
{
    public required EventGroupId Id { get; init; }
    public required string Title { get; init; }
    public required string Organiser { get; init; }
    public required string Place { get; init; }
    public required IReadOnlyList<Competition> Occurrences { get; init; }

    public bool IsRecurring => Occurrences.Count > 1;

    public Competition First => Occurrences[0];

    public DateOnly FirstDate => Occurrences.Min(c => c.Date);

    public DateOnly LastDate => Occurrences.Max(c => c.Date);

    public CompetitionLevel Level => Occurrences.Min(c => c.Level);

    public Discipline Discipline => First.Discipline;

    public Sport Sport => First.Sport;
}
