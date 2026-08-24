using Orientera.Backend.Arena;

namespace Orientera.Tests;

/// <summary>
/// Steg 5 av porten: prompten till bildmodellen mäts strängexakt mot prototypens utdata —
/// varje ordval i den är avsiktligt, så "nästan samma" är inte samma.
/// </summary>
public class ArenaPromptTests
{
    [Fact]
    public void Day_prompt_matches_the_prototype_verbatim()
    {
        var when = new DateTime(2026, 8, 24, 18, 30, 0);
        var (altitude, azimuth) = Sun.PositionOf(60.6032363729466, 16.9686012288786, when);
        var light = Lighting.At(altitude, azimuth);

        var prompt = EnhancementPrompt.Compose(
            "Trimtex Cup #4", "Gästrikland", ArenaSeason.Sommar, light, when,
            lamp: light.Night, wall: true);

        Assert.False(light.Night);
        Assert.Equal(File.ReadAllText(Fixture.PathFor("Arena", "prompt-day.txt")), prompt);
    }

    [Fact]
    public void Night_prompt_matches_the_prototype_verbatim()
    {
        var when = new DateTime(2026, 11, 14, 21, 0, 0);
        var (altitude, azimuth) = Sun.PositionOf(60.6032363729466, 16.9686012288786, when);
        var light = Lighting.At(altitude, azimuth);

        var prompt = EnhancementPrompt.Compose(
            "Trimtex Cup #4", "Gästrikland", ArenaSeason.Host, light, when,
            lamp: light.Night, wall: true);

        Assert.True(light.Night);
        Assert.Equal(File.ReadAllText(Fixture.PathFor("Arena", "prompt-night.txt")), prompt);
    }
}
