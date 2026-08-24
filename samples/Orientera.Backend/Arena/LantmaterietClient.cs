using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Orientera.Backend.Arena;

/// <summary>
/// HTTP-gränsen mot Lantmäteriet: STAC-katalogen över markhöjdmodellen, GeoTIFF:erna bakom
/// den, och WMS:erna för ortofoto och terrängskuggning.
/// </summary>
/// <remarks>
/// <c>dl1.lantmateriet.se</c> svarar sporadiskt 403 på fullt giltiga anrop — lastbalansering
/// där inte alla noder känner sessionen. Det går över på omförsök, så varje hämtning härifrån
/// återförsöker med växande paus i stället för att lita på första svaret.
///
/// Nedladdade filer läggs i en katalog på disk. Rutorna är stora och ändras aldrig, så
/// katalogen är ett arkiv snarare än en cache — i en Function överlever den varm instans
/// och kortar varje efterföljande rendering i samma trakt.
/// </remarks>
public sealed class LantmaterietClient(
    HttpClient _http,
    string _cacheDirectory,
    GeotorgetCredentials? _credentials,
    ILogger<LantmaterietClient> _logger)
{
    private const string Stac = "https://api.lantmateriet.se/stac-hojd/v1/search";
    private const string HeightWms = "https://minkarta.lantmateriet.se/map/hojdmodell/";
    private const string OrthoWms = "https://minkarta.lantmateriet.se/map/ortofoto/";

    private static readonly HashSet<HttpStatusCode> Transient =
    [
        HttpStatusCode.Forbidden,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    public bool HasCredentials => _credentials is not null;

    /// <summary>Adresserna till de höjdrutor som täcker en markrektangel.</summary>
    public async Task<IReadOnlyList<string>> SearchElevationTilesAsync(
        SwerefBounds bounds, CancellationToken cancellationToken)
    {
        var (lowLat, lowLon) = SwedishProjection.ToWgs84(bounds.MinX, bounds.MinY);
        var (highLat, highLon) = SwedishProjection.ToWgs84(bounds.MaxX, bounds.MaxY);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{Stac}?bbox={lowLon},{lowLat},{highLon},{highLat}&limit=100");

        var json = await RetryAsync(async token =>
        {
            using var response = await _http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(token);
        }, "STAC-sökningen", cancellationToken);

        using var document = JsonDocument.Parse(json);
        var hrefs = new List<string>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.GetProperty("collection").GetString()!.StartsWith("mhm-", StringComparison.Ordinal))
                continue;
            var href = feature.GetProperty("assets").GetProperty("data").GetProperty("href").GetString()!;
            if (href.EndsWith(".tif", StringComparison.Ordinal))
                hrefs.Add(href);
        }
        return hrefs;
    }

    /// <summary>Hämtar en höjdruta till disk och returnerar sökvägen. Finns den redan görs inget anrop.</summary>
    public async Task<string> DownloadElevationTileAsync(string href, CancellationToken cancellationToken)
    {
        var name = href[(href.LastIndexOf('/') + 1)..];
        var path = Path.Combine(_cacheDirectory, "hojd", name);
        if (new FileInfo(path) is { Exists: true, Length: > 0 })
            return path;

        if (_credentials is null)
            throw new InvalidOperationException(
                "höjdrutor kräver Geotorget-inloggning — sätt LM_USER/LM_PASS eller ~/.config/lantmateriet.env");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_credentials.User}:{_credentials.Password}")));

        await RetryAsync<object?>(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, href);
            request.Headers.Authorization = authorization;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    "Lantmäteriet avvisade inloggningen (401). Kontrollera behörigheten till "
                    + "Markhöjdmodell Nedladdning i Geotorget, och att den ligger på samma konto som LM_USER.");
            response.EnsureSuccessStatusCode();

            var partial = path + ".part";
            await using (var file = File.Create(partial))
                await response.Content.CopyToAsync(file, token);
            File.Move(partial, path, overwrite: true);
            return null;
        }, name, cancellationToken);

        return path;
    }

    /// <summary>
    /// Största bild minkartas WMS lämnar ut. Ett stort tävlingsområde ger ett grid över
    /// taket — Jarlkut'n var 4 600 px högt — och då hämtas bilden i block som sys ihop.
    /// </summary>
    private const int MaxWmsPixels = 4096;

    /// <summary>Lantmäteriets ortofoto, 0,25 m. CC BY 4.0 — attributionen måste följa bilden.</summary>
    public Task<ColorGrid> OrthophotoAsync(
        SwerefBounds bounds, int width, int height, CancellationToken cancellationToken) =>
        WmsGridAsync(OrthoWms, "Ortofoto_0.25", "image/jpeg", "orto", bounds, width, height, cancellationToken);

    /// <summary>
    /// Lantmäteriets terrängskuggning — härledd ur 1 m-modellen, alltså den verkliga
    /// markformen under skogen, även om vi bara får den som bild.
    /// </summary>
    public async Task<ScalarGrid> HillshadeAsync(
        SwerefBounds bounds, int width, int height, CancellationToken cancellationToken)
    {
        var color = await WmsGridAsync(HeightWms, "terrangskuggning", "image/png", "shade",
            bounds, width, height, cancellationToken);

        var grid = new ScalarGrid(width, height);
        for (var p = 0; p < grid.Values.Length; p++)
            grid.Values[p] = color.Values[p * 3];
        return grid;
    }

    /// <summary>
    /// En WMS-bild på målgridet, i ett anrop när den ryms och annars i block. Blockens
    /// kanter ligger på målgridets pixelgränser, så sömmarna blir pixelexakta.
    /// </summary>
    private async Task<ColorGrid> WmsGridAsync(
        string baseUrl, string layer, string format, string tag,
        SwerefBounds bounds, int width, int height, CancellationToken cancellationToken)
    {
        if (width <= MaxWmsPixels && height <= MaxWmsPixels)
        {
            var bytes = await WmsAsync(baseUrl, layer, bounds, width, height, format, tag, cancellationToken);
            return ColorGridImage.Decode(bytes).Resized(width, height);
        }

        var grid = new ColorGrid(width, height);
        var metersPerPixelX = bounds.Width / width;
        var metersPerPixelY = bounds.Height / height;
        for (var y0 = 0; y0 < height; y0 += MaxWmsPixels)
        {
            for (var x0 = 0; x0 < width; x0 += MaxWmsPixels)
            {
                var blockWidth = Math.Min(MaxWmsPixels, width - x0);
                var blockHeight = Math.Min(MaxWmsPixels, height - y0);
                var blockBounds = new SwerefBounds(
                    bounds.MinX + x0 * metersPerPixelX,
                    bounds.MaxY - (y0 + blockHeight) * metersPerPixelY,
                    bounds.MinX + (x0 + blockWidth) * metersPerPixelX,
                    bounds.MaxY - y0 * metersPerPixelY);

                var bytes = await WmsAsync(baseUrl, layer, blockBounds, blockWidth, blockHeight,
                    format, tag, cancellationToken);
                var block = ColorGridImage.Decode(bytes).Resized(blockWidth, blockHeight);
                for (var y = 0; y < blockHeight; y++)
                {
                    Array.Copy(block.Values, y * blockWidth * 3,
                        grid.Values, ((y0 + y) * width + x0) * 3, blockWidth * 3);
                }
            }
        }
        return grid;
    }

    private async Task<byte[]> WmsAsync(
        string baseUrl, string layer, SwerefBounds bounds, int width, int height,
        string format, string tag, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{tag}_{layer}_{(int)bounds.MinX}_{(int)bounds.MinY}_{width}x{height}.img");
        var path = Path.Combine(_cacheDirectory, key);
        if (new FileInfo(path) is { Exists: true, Length: > 0 })
            return await File.ReadAllBytesAsync(path, cancellationToken);

        var bbox = string.Create(CultureInfo.InvariantCulture,
            $"{bounds.MinX:F2},{bounds.MinY:F2},{bounds.MaxX:F2},{bounds.MaxY:F2}");
        var url = $"{baseUrl}?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS={Uri.EscapeDataString(layer)}"
            + $"&STYLES=&SRS={Uri.EscapeDataString("EPSG:3006")}&BBOX={Uri.EscapeDataString(bbox)}"
            + string.Create(CultureInfo.InvariantCulture, $"&WIDTH={width}&HEIGHT={height}")
            + $"&FORMAT={Uri.EscapeDataString(format)}";

        var bytes = await RetryAsync(async token =>
        {
            using var response = await _http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsByteArrayAsync(token);
            // WMS svarar 200 med ett XML-fel i stället för en statuskod när något är galet.
            if (response.Content.Headers.ContentType?.MediaType?.Contains("xml") == true)
                throw new InvalidOperationException(
                    $"WMS svarade med fel i stället för bild: {Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 300))}");
            return content;
        }, $"WMS {layer}", cancellationToken);

        Directory.CreateDirectory(_cacheDirectory);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return bytes;
    }

    private async Task<T> RetryAsync<T>(
        Func<CancellationToken, Task<T>> attempt, string what, CancellationToken cancellationToken)
    {
        const int tries = 8;
        for (var i = 0; ; i++)
        {
            try
            {
                return await attempt(cancellationToken);
            }
            catch (HttpRequestException exception) when (
                i < tries - 1
                && exception.StatusCode is { } status && Transient.Contains(status))
            {
                _logger.LogInformation("{What} svarade {Status}, försök {Attempt} av {Tries}.",
                    what, (int)status, i + 1, tries);
                await Task.Delay(TimeSpan.FromSeconds(1.5 * (i + 1)), cancellationToken);
            }
        }
    }
}
