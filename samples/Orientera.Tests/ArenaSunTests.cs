using System.Globalization;
using Orientera.Backend.Arena;

namespace Orientera.Tests;

/// <summary>Steg 2 av porten: solens läge och årstiden mot Python-prototypens facit.</summary>
public class ArenaSunTests
{
    [Fact]
    public void Solar_position_matches_the_reference()
    {
        var checkpoints = ArenaFacit.Checkpoints.Value;
        var arena = checkpoints.GetProperty("projektion").GetProperty("arena_wgs84");
        var when = DateTime.Parse(
            checkpoints.GetProperty("tavling").GetProperty("tid").GetString()!,
            CultureInfo.InvariantCulture);

        var facit = checkpoints.GetProperty("sol");
        var tolerance = facit.GetProperty("tolerans_grader").GetDouble();
        var (altitude, azimuth) = Sun.PositionOf(arena[0].GetDouble(), arena[1].GetDouble(), when);

        Assert.InRange(altitude, facit.GetProperty("hojd_grader").GetDouble() - tolerance,
                                 facit.GetProperty("hojd_grader").GetDouble() + tolerance);
        Assert.InRange(azimuth, facit.GetProperty("azimut_grader").GetDouble() - tolerance,
                                facit.GetProperty("azimut_grader").GetDouble() + tolerance);
    }

    [Fact]
    public void Season_matches_the_reference()
    {
        var checkpoints = ArenaFacit.Checkpoints.Value;
        var when = DateTime.Parse(
            checkpoints.GetProperty("tavling").GetProperty("tid").GetString()!,
            CultureInfo.InvariantCulture);

        Assert.Equal(checkpoints.GetProperty("sol").GetProperty("arstid").GetString(),
            ArenaImageKey.SeasonOf(when).ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Bilden visar ren dag eller ren natt. Skymningen däremellan gav dunkla bilder där
    /// terrängen — hela poängen — försvann i långa skuggor och orange grus.
    /// </summary>
    [Fact]
    public void Dusk_is_lit_as_daylight_and_a_night_race_as_night()
    {
        // Trimtex Cup #4, 24 augusti 18:30: solen står 12,5° över horisonten.
        var evening = Lighting.For(12.5, 270.1, nightRace: false);
        // Nattävling i juni: solen är bara några grader under horisonten klockan tio.
        var brightNight = Lighting.For(-3.0, 5.0, nightRace: true);
        var noon = Lighting.For(45.0, 180.0, nightRace: false);

        Assert.False(evening.Night);
        Assert.Equal(Lighting.DayFloor, evening.Altitude);
        Assert.Equal(270.1, evening.Azimuth);
        Assert.True(brightNight.Night);
        Assert.Equal(45.0, noon.Altitude);
    }

    /// <summary>Sommartiden vänder på sista söndagen, inte på månadsskiftet.</summary>
    [Theory]
    [InlineData(2026, 3, 28, 1)]
    [InlineData(2026, 3, 29, 2)]
    [InlineData(2026, 10, 24, 2)]
    [InlineData(2026, 10, 25, 1)]
    [InlineData(2027, 1, 15, 1)]
    [InlineData(2027, 7, 15, 2)]
    public void Swedish_clock_follows_the_last_sunday_rule(int year, int month, int day, int offset) =>
        Assert.Equal(offset, Sun.SwedishUtcOffset(new DateTime(year, month, day, 12, 0, 0)));
}
