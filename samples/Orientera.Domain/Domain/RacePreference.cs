namespace Orientera.Domain;

/// <summary>
/// One kind of race a runner would rather be at: a sport and a distance together.
/// </summary>
/// <remarks>
/// A pair and not two lists, because the two do not multiply out into things anyone actually
/// wants. Someone who likes indoor sprints has said nothing about sprints in a forest, and two
/// separate lists would have given them both.
/// </remarks>
/// <param name="Discipline">
/// Null where the sport does not race distances — indoor, trail-O, orienteering shooting. There
/// the sport is the whole preference, and a null matches every race in it.
/// </param>
public readonly record struct RacePreference(Sport Sport, Discipline? Discipline = null);
