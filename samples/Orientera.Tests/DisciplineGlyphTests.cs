using System.Text.RegularExpressions;
using Orientera.Presentation;

namespace Orientera.Tests;

/// <summary>
/// The discipline marks exist twice — as SVG files that were drawn and approved, and as path data
/// the app draws from. That is a copy, and a copy drifts. These tests are the seam: the app may
/// not draw a shape nobody looked at.
/// </summary>
public class DisciplineGlyphTests
{
    private static readonly string Folder = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "Orientera", "Resources", "Design", "Disciplines");

    private static readonly Dictionary<Discipline, string> Files = new()
    {
        [Discipline.Sprint] = "discipline_sprint.svg",
        [Discipline.Middle] = "discipline_middle.svg",
        [Discipline.Long] = "discipline_long.svg",
        [Discipline.UltraLong] = "discipline_ultralong.svg",
        [Discipline.Night] = "discipline_night.svg",
        [Discipline.Relay] = "discipline_relay.svg",
    };

    [Theory]
    [InlineData(Discipline.Sprint)]
    [InlineData(Discipline.Middle)]
    [InlineData(Discipline.Long)]
    [InlineData(Discipline.UltraLong)]
    [InlineData(Discipline.Night)]
    [InlineData(Discipline.Relay)]
    public void The_app_draws_exactly_what_the_design_file_draws(Discipline discipline)
    {
        var svg = File.ReadAllText(Path.Combine(Folder, Files[discipline]));

        var drawn = Regex.Match(svg, """ d="([^"]+)" """.Trim()).Groups[1].Value;

        Assert.Equal(drawn, DisciplineGlyph.Path(discipline));
    }

    [Fact]
    public void The_championship_cup_is_drawn_exactly_as_designed()
    {
        var svg = File.ReadAllText(Path.Combine(Folder, "level_championship.svg"));

        var drawn = Regex.Match(svg, """ d="([^"]+)" """.Trim()).Groups[1].Value;

        Assert.Equal(drawn, DisciplineGlyph.LevelPath(CompetitionLevel.Championship));
    }

    /// <summary>
    /// Only a championship carries a mark. The rest of the ladder differs by degree, and a caption
    /// line with a symbol on every level is a line nobody reads.
    /// </summary>
    [Fact]
    public void No_other_level_gets_a_mark()
    {
        var others = Enum.GetValues<CompetitionLevel>().Where(l => l != CompetitionLevel.Championship);

        Assert.All(others, level => Assert.Equal(string.Empty, DisciplineGlyph.LevelPath(level)));
    }

    /// <summary>Every discipline has a mark; a missing one shows as nothing at all in the list.</summary>
    [Fact]
    public void No_discipline_is_left_without_a_mark()
    {
        foreach (var discipline in Enum.GetValues<Discipline>())
            Assert.NotEqual(string.Empty, DisciplineGlyph.Path(discipline));
    }

    /// <summary>
    /// The marks share one control ring in one place, so a list of them lines up rather than
    /// wobbling. Every distance is a route to the same control — which became true of all of them
    /// when indoor moved to the sport axis and took its roof with it.
    /// </summary>
    [Fact]
    public void Every_route_ends_at_the_same_control()
    {
        var routes = Enum.GetValues<Discipline>().Select(DisciplineGlyph.Path);

        Assert.All(routes, path => Assert.Contains("M17,6.8 m-3.4,0", path));
    }
}
