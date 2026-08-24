using System.Globalization;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;

namespace Orientera.Backend.Arena;

/// <summary>
/// En höjdruta ur Lantmäteriets markhöjdmodell: en kaklad float32-GeoTIFF med pyramider.
/// </summary>
/// <remarks>
/// Läsaren gör bara det rutorna behöver — kaklad float32, en kanal, DEFLATE — och vägrar högt
/// på allt annat, hellre än att tyst tolka fel. Georeferensen sitter i taggarna 33550
/// (pixelstorlek) och 33922 (fästpunkt); fästpunkten anger övre vänstra <em>hörnet</em> av
/// pixel (0,0), så pixelcentrum ligger en halv pixel in.
///
/// Pyramiderna finns för att en utzoomad vy inte ska läsa tio gånger mer data än den visar:
/// vid grövre målupplösning läses närmast finare nivå i stället för full upplösning.
/// </remarks>
public sealed class ElevationTile : IDisposable
{
    // Vissa regioners rutor har osorterade taggkataloger, och LibTiff varnar då för varje
    // tagg i varje IFD — hundratals rader per ruta som dränker loggen. Läsningen är rätt
    // ändå (korsmätt mot rasterio på samma ruta), så varningskanalen tystas; riktiga fel
    // går fortfarande genom felhanteraren och blir undantag.
    static ElevationTile() => Tiff.SetErrorHandler(new QuietWarnings());

    private sealed class QuietWarnings : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }

    private readonly Tiff _tiff;
    private readonly (int Width, int Height)[] _levels;

    public double OriginX { get; }

    /// <summary>Norrkanten — y växer söderut i pixelled.</summary>
    public double OriginY { get; }

    public double Resolution { get; }
    public int Width { get; }
    public int Height { get; }
    public float NoData { get; }

    private ElevationTile(Tiff tiff)
    {
        _tiff = tiff;

        var scale = Doubles(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
        var tie = Doubles(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG);
        if (scale.Length < 2 || tie.Length < 6)
            throw new InvalidDataException("höjdrutan saknar georeferens (tagg 33550/33922)");
        if (Math.Abs(scale[0] - scale[1]) > 1e-9)
            throw new InvalidDataException("höjdrutan har olika upplösning i x och y");

        Resolution = scale[0];
        OriginX = tie[3] - tie[0] * scale[0];
        OriginY = tie[4] + tie[1] * scale[1];

        var nodataField = tiff.GetField(TiffTag.GDAL_NODATA);
        NoData = nodataField is { Length: > 1 }
            && float.TryParse(nodataField[1].ToString()?.TrimEnd('\0'),
                              NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -9999f;

        _levels = new (int, int)[tiff.NumberOfDirectories()];
        for (short i = 0; i < _levels.Length; i++)
        {
            tiff.SetDirectory(i);
            _levels[i] = (Int(tiff, TiffTag.IMAGEWIDTH), Int(tiff, TiffTag.IMAGELENGTH));
        }
        (Width, Height) = _levels[0];
        tiff.SetDirectory(0);
    }

    public static ElevationTile Open(string path)
    {
        var tiff = Tiff.Open(path, "r")
            ?? throw new InvalidDataException($"kunde inte öppna {Path.GetFileName(path)} som TIFF");
        try
        {
            return new ElevationTile(tiff);
        }
        catch
        {
            tiff.Dispose();
            throw;
        }
    }

    public int LevelCount => _levels.Length;

    public (int Width, int Height) SizeAt(int level) => _levels[level];

    /// <summary>Meter per pixel på en pyramidnivå, härledd ur nivåns bredd.</summary>
    public double ResolutionAt(int level) => Resolution * Width / _levels[level].Width;

    /// <summary>
    /// Finaste nivå som inte är finare än målupplösningen behöver — grövre än så vore att
    /// tappa data, finare vore att läsa i onödan.
    /// </summary>
    public int LevelFor(double targetResolution)
    {
        var level = 0;
        for (var i = 1; i < _levels.Length; i++)
        {
            if (ResolutionAt(i) <= targetResolution + 1e-9)
                level = i;
        }
        return level;
    }

    /// <summary>
    /// Läser ett pixelfönster på en pyramidnivå. Fönstret kläms mot nivåns kanter; höjder
    /// utanför, och nodata, blir NaN.
    /// </summary>
    public ScalarGrid Read(int level, int column, int row, int columns, int rows)
    {
        var grid = new ScalarGrid(columns, rows).Fill(float.NaN);

        _tiff.SetDirectory((short)level);
        Require(Int(_tiff, TiffTag.BITSPERSAMPLE) == 32, "32 bitar per sampel");
        Require(Int(_tiff, TiffTag.SAMPLESPERPIXEL, 1) == 1, "en kanal");
        Require(Int(_tiff, TiffTag.SAMPLEFORMAT, 1) == (int)SampleFormat.IEEEFP, "flyttalssampel");
        Require(_tiff.IsTiled(), "kaklad läggning");

        var (levelWidth, levelHeight) = _levels[level];
        var tileWidth = Int(_tiff, TiffTag.TILEWIDTH);
        var tileHeight = Int(_tiff, TiffTag.TILELENGTH);

        var x0 = Math.Max(column, 0);
        var y0 = Math.Max(row, 0);
        var x1 = Math.Min(column + columns, levelWidth);
        var y1 = Math.Min(row + rows, levelHeight);
        if (x0 >= x1 || y0 >= y1)
            return grid;

        var buffer = new byte[_tiff.TileSize()];
        for (var tileY = y0 / tileHeight * tileHeight; tileY < y1; tileY += tileHeight)
        {
            for (var tileX = x0 / tileWidth * tileWidth; tileX < x1; tileX += tileWidth)
            {
                if (_tiff.ReadTile(buffer, 0, tileX, tileY, 0, 0) < 0)
                    throw new InvalidDataException($"kakel ({tileX},{tileY}) gick inte att avkoda");
                var samples = MemoryMarshal.Cast<byte, float>(buffer);

                var copyX0 = Math.Max(x0, tileX);
                var copyX1 = Math.Min(x1, tileX + tileWidth);
                var copyY0 = Math.Max(y0, tileY);
                var copyY1 = Math.Min(y1, tileY + tileHeight);
                for (var y = copyY0; y < copyY1; y++)
                {
                    var source = samples.Slice((y - tileY) * tileWidth + (copyX0 - tileX), copyX1 - copyX0);
                    var target = grid.Values.AsSpan((y - row) * columns + (copyX0 - column), copyX1 - copyX0);
                    for (var i = 0; i < source.Length; i++)
                        target[i] = source[i] == NoData || source[i] < -1000f ? float.NaN : source[i];
                }
            }
        }
        return grid;
    }

    private static void Require(bool condition, string expectation)
    {
        if (!condition)
            throw new InvalidDataException($"höjdrutan har inte {expectation} — läsaren är skriven för Lantmäteriets COG:er");
    }

    private static int Int(Tiff tiff, TiffTag tag, int? fallback = null)
    {
        var field = tiff.GetField(tag);
        return field is { Length: > 0 }
            ? field[0].ToInt()
            : fallback ?? throw new InvalidDataException($"höjdrutan saknar tagg {tag}");
    }

    private static double[] Doubles(Tiff tiff, TiffTag tag)
    {
        var field = tiff.GetField(tag);
        if (field is not { Length: > 1 })
            return [];
        var bytes = field[1].GetBytes();
        var doubles = new double[bytes.Length / 8];
        Buffer.BlockCopy(bytes, 0, doubles, 0, doubles.Length * 8);
        return doubles;
    }

    public void Dispose() => _tiff.Dispose();
}
