using Microsoft.Extensions.Logging;

namespace Orientera.Backend.Arena;

/// <summary>
/// Terrängdata för ett tävlingsområde: höjdmodellen mosaikad till bildens grid, ortofotot
/// och terrängskuggningen på samma grid.
/// </summary>
/// <remarks>
/// Mosaiken görs för hand i stället för av GDAL: rutorna klistras först in i ett gemensamt
/// fullupplöst fält — de ligger på samma metergitter, så inklistringen är exakt — och
/// samplas sedan bilinjärt i målgridets pixelcentra. Grövre målupplösning läser ur
/// pyramiderna i stället för full upplösning.
/// </remarks>
public sealed class TerrainSource(LantmaterietClient _client, ILogger<TerrainSource> _logger)
{
    /// <summary>
    /// Lantmäteriets 1 m markhöjdmodell över en markrektangel, samplad till <paramref name="width"/>
    /// gånger <paramref name="height"/> pixlar. <c>null</c> när inloggning saknas — anroparen
    /// får falla tillbaka på något grövre, inte krascha.
    /// </summary>
    public async Task<ScalarGrid?> ElevationAsync(
        SwerefBounds bounds, int width, int height, CancellationToken cancellationToken)
    {
        if (!_client.HasCredentials)
            return null;

        var hrefs = await _client.SearchElevationTilesAsync(bounds, cancellationToken);
        if (hrefs.Count == 0)
            return null;

        var paths = new string[hrefs.Count];
        await Parallel.ForAsync(0, hrefs.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (i, token) => paths[i] = await _client.DownloadElevationTileAsync(hrefs[i], token));

        var tiles = new List<ElevationTile>(paths.Length);
        try
        {
            foreach (var path in paths)
                tiles.Add(ElevationTile.Open(path));

            var grid = Mosaic(tiles, bounds, width, height);
            _logger.LogInformation("Höjdmodell {Width}x{Height} ur {Tiles} rutor.", width, height, tiles.Count);
            return grid;
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }
    }

    /// <summary>Mosaik och omsampling, skild från hämtningen så den kan mätas mot facit utan nät.</summary>
    public static ScalarGrid Mosaic(
        IReadOnlyList<ElevationTile> tiles, SwerefBounds bounds, int width, int height)
    {
        var targetResolution = bounds.Width / width;
        var level = tiles[0].LevelFor(targetResolution);
        var resolution = tiles[0].ResolutionAt(level);

        // Ett gemensamt fullupplöst fält över ramen plus en marginal för interpolationen.
        // Origo läggs på rutornas eget gitter så inklistringen blir pixelexakt.
        var margin = 2 * resolution;
        var originX = Math.Floor((bounds.MinX - margin) / resolution) * resolution;
        var originY = Math.Ceiling((bounds.MaxY + margin) / resolution) * resolution;
        var columns = (int)Math.Ceiling((bounds.MaxX + margin - originX) / resolution);
        var rows = (int)Math.Ceiling((originY - (bounds.MinY - margin)) / resolution);
        var mosaic = new ScalarGrid(columns, rows).Fill(float.NaN);

        foreach (var tile in tiles)
        {
            if (Math.Abs(tile.ResolutionAt(level) - resolution) > 1e-6)
                throw new InvalidDataException("höjdrutorna har olika upplösning — mosaiken förutsätter samma gitter");

            var (tileColumns, tileRows) = tile.SizeAt(level);
            // Mosaikens pixel (0,0) uttryckt i rutans pixelrum. Norr är rad noll i båda,
            // så raderna räknas från rutans nordkant precis som kolumnerna från västkanten.
            var offsetX = (int)Math.Round((originX - tile.OriginX) / resolution);
            var offsetY = (int)Math.Round((tile.OriginY - originY) / resolution);

            // Fönstret i rutans pixelrum som täcker mosaiken; Read klämmer mot rutans kanter.
            var readColumn = Math.Max(0, offsetX);
            var readRow = Math.Max(0, offsetY);
            var readColumns = Math.Min(tileColumns, offsetX + columns) - readColumn;
            var readRows = Math.Min(tileRows, offsetY + rows) - readRow;
            if (readColumns <= 0 || readRows <= 0)
                continue;

            var region = tile.Read(level, readColumn, readRow, readColumns, readRows);
            for (var y = 0; y < readRows; y++)
            {
                var target = (readRow - offsetY + y) * columns + (readColumn - offsetX);
                for (var x = 0; x < readColumns; x++)
                {
                    var value = region.Values[y * readColumns + x];
                    if (float.IsFinite(value))
                        mosaic.Values[target + x] = value;
                }
            }
        }

        // Bilinjär omsampling i pixelcentra, samma konvention som GDAL: målpixelns centrum
        // projiceras in i mosaiken och en halv pixel dras av för att hamna mellan centra.
        // Prototypen härleder upplösningen ur bredden och låter den gälla båda axlarna —
        // samma här, annars glider samplingspositionerna någon centimeter mot facit.
        var dem = new ScalarGrid(width, height);
        Parallel.For(0, height, y =>
        {
            var worldY = bounds.MaxY - (y + 0.5) * targetResolution;
            var pixelY = (originY - worldY) / resolution - 0.5;
            for (var x = 0; x < width; x++)
            {
                var worldX = bounds.MinX + (x + 0.5) * targetResolution;
                dem[x, y] = mosaic.Sample((worldX - originX) / resolution - 0.5, pixelY);
            }
        });

        // Hål — vatten, kanter — fylls med medianen, samma grepp som prototypen: en neutral
        // marknivå i stället för en grop som skuggningen skulle förstärka.
        var median = dem.MedianOfFinite();
        for (var i = 0; i < dem.Values.Length; i++)
        {
            if (!float.IsFinite(dem.Values[i]))
                dem.Values[i] = median;
        }
        return dem;
    }
}
