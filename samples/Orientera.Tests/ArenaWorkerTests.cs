using System.Text.Json;
using Orientera.Backend.Arena;

namespace Orientera.Tests;

/// <summary>Steg 6 av porten: sidläsningen, murkontrollen och beställningens väg genom kön.</summary>
public class ArenaWorkerTests
{
    [Fact]
    public void Arena_page_parsing_matches_the_prototype()
    {
        var facts = EventorArenaPage.Parse(
            File.ReadAllText(Fixture.PathFor("Arena", "eventor-59691.html")));

        var expected = JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(Fixture.PathFor("Arena", "eventor-59691.json")));

        Assert.NotNull(facts.Arena);
        Assert.Equal(expected.GetProperty("arena")[0].GetDouble(), facts.Arena!.Value.Latitude, 12);
        Assert.Equal(expected.GetProperty("arena")[1].GetDouble(), facts.Arena.Value.Longitude, 12);

        Assert.NotNull(facts.Area);
        var expectedArea = expected.GetProperty("area").EnumerateArray().ToList();
        Assert.Equal(expectedArea.Count, facts.Area!.Count);
        for (var i = 0; i < expectedArea.Count; i++)
        {
            Assert.Equal(expectedArea[i][0].GetDouble(), facts.Area[i].Latitude, 12);
            Assert.Equal(expectedArea[i][1].GetDouble(), facts.Area[i].Longitude, 12);
        }
    }

    [Fact]
    public void Page_without_polygon_yields_no_area()
    {
        var facts = EventorArenaPage.Parse(
            "<html>centerLatitude&quot;:&quot;59.5&quot; centerLongitude&quot;:&quot;15.5&quot;</html>");

        Assert.Equal((59.5, 15.5), facts.Arena);
        Assert.Null(facts.Area);
    }

    [Fact]
    public void Wall_check_passes_where_the_wall_still_stands()
    {
        var image = new ColorGrid(200, 100);
        var quads = new List<(double, (double X, double Y)[], (double X, double Y)[])>
        {
            (100.0, [(20.0, 20.0), (60.0, 22.0), (60.0, 50.0), (20.0, 48.0)], [(20.0, 20.0), (60.0, 22.0)]),
            (120.0, [(60.0, 22.0), (110.0, 25.0), (110.0, 55.0), (60.0, 50.0)], [(60.0, 22.0), (110.0, 25.0)]),
        };
        Overlays.DrawWall(image, quads);

        Assert.True(WallCheck.Survived(image, quads, out var coverage));
        Assert.Equal(1.0, coverage, 3);
    }

    [Fact]
    public void Wall_check_fails_where_the_model_painted_the_wall_away()
    {
        // Grön skog där muren skulle stått: modellen har målat bort den.
        var image = new ColorGrid(200, 100);
        for (var i = 0; i < image.Values.Length; i += 3)
        {
            image.Values[i] = 0.20f;
            image.Values[i + 1] = 0.45f;
            image.Values[i + 2] = 0.15f;
        }
        var quads = new List<(double, (double X, double Y)[], (double X, double Y)[])>
        {
            (100.0, [(20.0, 20.0), (60.0, 22.0), (60.0, 50.0), (20.0, 48.0)], [(20.0, 20.0), (60.0, 22.0)]),
        };

        Assert.False(WallCheck.Survived(image, quads, out var coverage));
        Assert.Equal(0.0, coverage, 3);
    }

    /// <summary>Vimpeln är en inbäddad bildfil — det här fångar en tappad resurs vid bygget.</summary>
    [Fact]
    public void Flag_asset_composites_onto_the_image()
    {
        var image = new ColorGrid(400, 400);
        Flag.Draw(image, (200, 350), 150);

        var painted = image.Values.Count(v => v > 0.1f);
        Assert.True(painted > 500, $"bara {painted} kanalvärden målades — vimpeln saknas i bilden");
    }

    [Fact]
    public void Indoor_prompt_is_stable_per_competition()
    {
        Assert.Equal(IndoorPrompt.For("12345"), IndoorPrompt.For("12345"));
        Assert.Contains("orienteering control marker", IndoorPrompt.For("12345"));
        Assert.Contains("No people in the frame", IndoorPrompt.For("12345"));
    }

    /// <summary>Beställningen serialiseras av store och läses av arbetaren — samma form i båda ändar.</summary>
    [Fact]
    public void Order_survives_the_queue_round_trip()
    {
        var key = new ArenaImageKey("59691", ArenaSeason.Sommar, Night: false, Version: 1);

        var roundTripped = JsonSerializer.Deserialize<ArenaImageKey>(JsonSerializer.Serialize(key));

        Assert.Equal(key, roundTripped);
        Assert.Equal("v1/59691-sommar.png", roundTripped.BlobName);
    }
}
