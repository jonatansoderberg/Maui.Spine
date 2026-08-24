namespace Orientera.Domain;

/// <summary>
/// One kind of race a runner would rather be at: a sport and a distance together.
/// </summary>
/// <remarks>
/// A pair and not two lists, because the two do not multiply out into things anyone actually
/// wants. Someone who likes indoor sprints has said nothing about sprints in a forest, and two
/// separate lists would have given them both.
/// </remarks>
public readonly record struct RacePreference(Sport Sport, Discipline Discipline);
