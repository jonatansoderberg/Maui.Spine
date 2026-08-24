using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orientera.Backend.Arena;

namespace Orientera.Tests;

/// <summary>
/// Steg 3 och 4 av porten mäts här: hela den nakna renderingen — terräng, ljus, mur — mot
/// prototypens referensbild. Kantkorrelationen jämför var strukturerna sitter, inte exakta
/// färgvärden, och det är den som avslöjar en geometri som glidit.
/// </summary>
public class ArenaImageFacitTests
{
    [ArenaFacit.SceneFact]
    public void Bare_render_matches_the_reference_image()
    {
        var fixture = JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(Fixture.PathFor("Arena", "eventor-59691.json")));
        var arena = (fixture.GetProperty("arena")[0].GetDouble(), fixture.GetProperty("arena")[1].GetDouble());
        var area = fixture.GetProperty("area").EnumerateArray()
            .Select(p => (p[0].GetDouble(), p[1].GetDouble()))
            .ToList();
        var geometry = ArenaGeometry.From(arena, area);
        Assert.True(geometry.HasOutline);

        var checkpoints = ArenaFacit.Checkpoints.Value;
        var frame = checkpoints.GetProperty("ram_sweref99tm");
        // Områdespassningen är på, som i produktion. För referensområdet biter ingen av
        // begränsningarna — ramen ska förbli exakt facits — och det är just det som mäts här.
        var bounds = TerrainRenderer.FrameBounds(
            geometry.Area, CameraSettings.Default, ArenaComposer.Width, ArenaComposer.Height,
            fitArea: true);
        Assert.InRange(bounds.MinX, frame[0].GetDouble() - 0.02, frame[0].GetDouble() + 0.02);
        Assert.InRange(bounds.MinY, frame[1].GetDouble() - 0.02, frame[1].GetDouble() + 0.02);
        Assert.InRange(bounds.MaxX, frame[2].GetDouble() - 0.02, frame[2].GetDouble() + 0.02);
        Assert.InRange(bounds.MaxY, frame[3].GetDouble() - 0.02, frame[3].GetDouble() + 0.02);

        var gridWidth = (int)(bounds.Width / TerrainRenderer.GroundResolution);
        var gridHeight = (int)(bounds.Height / TerrainRenderer.GroundResolution);

        var tiles = Directory.GetFiles(ArenaFacit.ElevationCache, "*.tif")
            .Order()
            .Select(ElevationTile.Open)
            .ToList();
        ScalarGrid elevation;
        try
        {
            elevation = TerrainSource.Mosaic(tiles, bounds, gridWidth, gridHeight);
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }

        var client = new LantmaterietClient(new HttpClient(),
            Path.Combine(ArenaFacit.RepoRoot, "tools", "arenabild", "cache"),
            null, NullLogger<LantmaterietClient>.Instance);
        var orthophoto = client.OrthophotoAsync(bounds, gridWidth, gridHeight, CancellationToken.None)
            .GetAwaiter().GetResult();

        var when = DateTime.Parse(
            checkpoints.GetProperty("tavling").GetProperty("tid").GetString()!, CultureInfo.InvariantCulture);
        var (altitude, azimuth) = Sun.PositionOf(arena.Item1, arena.Item2, when);
        var light = Lighting.At(altitude, azimuth);

        var scene = ArenaComposer.ComposeBare(
            geometry, ArenaSeason.Sommar, light, bounds, elevation, orthophoto,
            ArenaComposer.Width, ArenaComposer.Height);
        Assert.NotNull(scene.WallQuads);

        var reference = ColorGridImage.Decode(File.ReadAllBytes(Path.Combine(
            ArenaFacit.RepoRoot, "tools", "arenabild", "referens",
            checkpoints.GetProperty("bild").GetProperty("fil").GetString()!)));
        var rendered = scene.Image.Resized(reference.Width, reference.Height);

        var producedPath = Path.Combine(Path.GetTempPath(), "arenabild-port-naken.png");
        File.WriteAllBytes(producedPath, rendered.ToPng());

        var correlation = EdgeCorrelation(rendered, reference);
        File.WriteAllText(Path.ChangeExtension(producedPath, ".txt"),
            correlation.ToString("F5", CultureInfo.InvariantCulture));
        Assert.True(correlation > 0.98,
            $"kantkorrelationen är {correlation:F4}, kravet är > 0.98 — porten avviker ({producedPath})");
    }

    /// <summary>Pearsonkorrelation mellan bildernas Sobel-gradienter, räknad på luminansen.</summary>
    private static double EdgeCorrelation(ColorGrid a, ColorGrid b)
    {
        var edgesA = SobelMagnitude(a);
        var edgesB = SobelMagnitude(b);

        double sumA = 0, sumB = 0;
        for (var i = 0; i < edgesA.Length; i++)
        {
            sumA += edgesA[i];
            sumB += edgesB[i];
        }
        var meanA = sumA / edgesA.Length;
        var meanB = sumB / edgesB.Length;

        double covariance = 0, varianceA = 0, varianceB = 0;
        for (var i = 0; i < edgesA.Length; i++)
        {
            var da = edgesA[i] - meanA;
            var db = edgesB[i] - meanB;
            covariance += da * db;
            varianceA += da * da;
            varianceB += db * db;
        }
        return covariance / Math.Sqrt(varianceA * varianceB);
    }

    private static double[] SobelMagnitude(ColorGrid image)
    {
        var width = image.Width;
        var height = image.Height;
        var gray = new double[width * height];
        for (var p = 0; p < gray.Length; p++)
            gray[p] = TerrainTexture.Luminance(
                image.Values[p * 3], image.Values[p * 3 + 1], image.Values[p * 3 + 2]);

        var edges = new double[width * height];
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var p = y * width + x;
                var gx = gray[p - width + 1] + 2 * gray[p + 1] + gray[p + width + 1]
                       - gray[p - width - 1] - 2 * gray[p - 1] - gray[p + width - 1];
                var gy = gray[p + width - 1] + 2 * gray[p + width] + gray[p + width + 1]
                       - gray[p - width - 1] - 2 * gray[p - width] - gray[p - width + 1];
                edges[p] = Math.Sqrt(gx * gx + gy * gy);
            }
        }
        return edges;
    }
}
