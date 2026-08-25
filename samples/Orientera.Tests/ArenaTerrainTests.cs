using Orientera.Backend.Arena;

namespace Orientera.Tests;

/// <summary>
/// Steg 1 av porten mäts här: projektionen och höjdmosaiken mot Python-prototypens facit.
/// </summary>
public class ArenaTerrainTests
{
    [Fact]
    public void Projection_matches_pyproj_within_a_metre()
    {
        var facit = ArenaFacit.Checkpoints.Value.GetProperty("projektion");
        var wgs84 = facit.GetProperty("arena_wgs84");
        var expected = facit.GetProperty("arena_sweref99tm");
        var tolerance = facit.GetProperty("tolerans_m").GetDouble();

        var (east, north) = SwedishProjection.ToSweref(
            wgs84[0].GetDouble(), wgs84[1].GetDouble());

        Assert.InRange(east, expected[0].GetDouble() - tolerance, expected[0].GetDouble() + tolerance);
        Assert.InRange(north, expected[1].GetDouble() - tolerance, expected[1].GetDouble() + tolerance);
    }

    [Fact]
    public void Projection_makes_the_round_trip()
    {
        var (east, north) = SwedishProjection.ToSweref(60.6032363729466, 16.9686012288786);
        var (latitude, longitude) = SwedishProjection.ToWgs84(east, north);

        Assert.InRange(latitude, 60.6032363729466 - 1e-7, 60.6032363729466 + 1e-7);
        Assert.InRange(longitude, 16.9686012288786 - 1e-7, 16.9686012288786 + 1e-7);
    }

    /// <summary>
    /// Mosaiken och den bilinjära omsamplingen mot rasterios resultat. Statistiken är känslig
    /// för pixelcentrumkonventionen — en halvpixel fel syns direkt i medelvärdet.
    /// </summary>
    [ArenaFacit.ElevationFact]
    public void Elevation_mosaic_matches_the_reference_statistics()
    {
        var checkpoints = ArenaFacit.Checkpoints.Value;
        var frame = checkpoints.GetProperty("ram_sweref99tm");
        var bounds = new SwerefBounds(
            frame[0].GetDouble(), frame[1].GetDouble(), frame[2].GetDouble(), frame[3].GetDouble());

        var facit = checkpoints.GetProperty("hojdmodell");
        var resolution = facit.GetProperty("upplosning_m").GetDouble();
        var width = (int)(bounds.Width / resolution);
        var height = (int)(bounds.Height / resolution);
        Assert.Equal(facit.GetProperty("grid")[0].GetInt32(), height);
        Assert.Equal(facit.GetProperty("grid")[1].GetInt32(), width);

        var tiles = Directory.GetFiles(ArenaFacit.ElevationCache, "*.tif")
            .Order()
            .Select(ElevationTile.Open)
            .ToList();
        try
        {
            var dem = TerrainSource.Mosaic(tiles, bounds, width, height);

            double sum = 0, squares = 0;
            double min = double.MaxValue, max = double.MinValue;
            foreach (var value in dem.Values)
            {
                sum += value;
                squares += (double)value * value;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            var mean = sum / dem.Values.Length;
            var std = Math.Sqrt(squares / dem.Values.Length - mean * mean);

            var tolerance = facit.GetProperty("tolerans_m").GetDouble();
            Assert.InRange(min, facit.GetProperty("min_m").GetDouble() - tolerance,
                                facit.GetProperty("min_m").GetDouble() + tolerance);
            Assert.InRange(max, facit.GetProperty("max_m").GetDouble() - tolerance,
                                facit.GetProperty("max_m").GetDouble() + tolerance);
            Assert.InRange(mean, facit.GetProperty("medel_m").GetDouble() - tolerance,
                                 facit.GetProperty("medel_m").GetDouble() + tolerance);
            Assert.InRange(std, facit.GetProperty("std_m").GetDouble() - tolerance,
                                facit.GetProperty("std_m").GetDouble() + tolerance);
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }
    }
}
