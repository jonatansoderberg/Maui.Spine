using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;

namespace Orientera.Presentation;

/// <summary>
/// <see cref="DisciplineGlyph"/>'s path data as something a <c>Path</c> can draw.
/// </summary>
/// <remarks>
/// A binding hands the target property whatever the source holds; unlike a literal in XAML it does
/// not run the property's type converter on the way. Binding the path string straight to
/// <c>Path.Data</c> therefore drew nothing at all — no error, no warning, a correctly sized blank
/// where the mark should be. The conversion happens here instead, once, and the view models hand
/// out geometry rather than text.
/// <para>
/// Separate from <see cref="DisciplineGlyph"/> because that file is compiled into the test project,
/// which is MAUI-free by construction. The shapes are testable; the drawing of them is not.
/// </para>
/// </remarks>
public static class DisciplineShape
{
    private static readonly PathGeometryConverter Converter = new();

    private static readonly Dictionary<Discipline, Geometry?> Cache = [];

    /// <summary>The mark for a discipline, or null when there is nothing to draw.</summary>
    /// <remarks>
    /// Cached because a list rebuild asks for the same seven shapes on every row, and parsing a
    /// path is not free.
    /// </remarks>
    public static Geometry? For(Discipline discipline)
    {
        if (Cache.TryGetValue(discipline, out var cached))
            return cached;

        var geometry = DisciplineGlyph.Path(discipline) is { Length: > 0 } data
            ? Converter.ConvertFromInvariantString(data) as Geometry
            : null;

        return Cache[discipline] = geometry;
    }

    /// <summary>The mark for a discipline that may not be known.</summary>
    public static Geometry? For(Discipline? discipline) =>
        discipline is { } known ? For(known) : null;

    private static readonly Dictionary<CompetitionLevel, Geometry?> LevelCache = [];

    /// <summary>The mark for a competition's level, or null when the level has none.</summary>
    public static Geometry? For(CompetitionLevel level)
    {
        if (LevelCache.TryGetValue(level, out var cached))
            return cached;

        var geometry = DisciplineGlyph.LevelPath(level) is { Length: > 0 } data
            ? Converter.ConvertFromInvariantString(data) as Geometry
            : null;

        return LevelCache[level] = geometry;
    }
}
