using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Arena;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.Functions;

/// <summary>
/// Arbetaren som gör arenabilderna. Läser beställningarna <see cref="Arena.ArenaImageStore"/>
/// lägger på kön, renderar, ljussätter genom bildmodellen och skriver blobben som store
/// sedan serverar.
/// </summary>
/// <remarks>
/// Ett fel är ett giltigt utfall: kastas ett undantag går beställningen tillbaka på kön och
/// försöks om, och efter värdens maxförsök hamnar den i giftkön i stället för att blockera
/// andra beställningar. Saknad konfiguration är däremot inte värt att försöka om — den blir
/// inte bättre av att vänta — så den loggas och släpps.
/// </remarks>
public sealed class ArenaImageWorker(
    EventorSource _source,
    EventorArenaPage _page,
    ArenaComposer _composer,
    IOptions<ArenaImageOptions> _options,
    ILoggerFactory _loggers,
    ILogger<ArenaImageWorker> _logger)
{
    [Function("MakeArenaImage")]
    public async Task Run(
        // Könamnet är detsamma som ArenaImageOptions.Queue har som standard — attributet
        // kräver en konstant, så en omdöpt kö måste döpas om på båda ställena.
        [QueueTrigger("arenabilder-att-gora", Connection = "ArenaImage:ConnectionString")] string message,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.IsConfigured)
        {
            _logger.LogWarning("Arenabilder är inte konfigurerade — beställningen släpps.");
            return;
        }

        var apiKey = ImageModel.FindApiKey();
        if (apiKey is null)
        {
            _logger.LogWarning("Ingen OpenAI-nyckel — beställningen släpps. Sätt OPENAI_API_KEY.");
            return;
        }

        var key = JsonSerializer.Deserialize<ArenaImageKey>(message);
        var model = new ImageModel(
            new OpenAI.Images.ImageClient(options.ImageModel, apiKey),
            _loggers.CreateLogger<ImageModel>());

        var png = key.Season == ArenaSeason.Inomhus
            ? await model.GenerateAsync(IndoorPrompt.For(key.EventId), cancellationToken)
            : await MakeTerrainImageAsync(key, model, cancellationToken);
        if (png is null)
            return;

        // Publik blobläsning: appen hämtar bilderna direkt på sina urlar, utan nycklar.
        // Utan detta skapas containern privat och varje hämtning svarar 403 — bilden ser
        // aldrig ut att bli klar fast den ligger där. Lagringskontot måste också tillåta
        // publik åtkomst (AllowBlobPublicAccess), annars ignoreras begäran tyst.
        var container = new BlobContainerClient(options.ConnectionString, options.Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        await container.GetBlobClient(key.BlobName).UploadAsync(
            new BinaryData(png),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" } },
            cancellationToken);

        _logger.LogInformation("Arenabild klar ({Blob}).", key.BlobName);
    }

    private async Task<byte[]?> MakeTerrainImageAsync(
        ArenaImageKey key, ImageModel model, CancellationToken cancellationToken)
    {
        var competition = await _source.GetCompetitionAsync(new CompetitionId(key.EventId), cancellationToken);
        if (competition is null)
        {
            _logger.LogWarning("Tävlingen {EventId} finns inte längre i Eventor — beställningen släpps.", key.EventId);
            return null;
        }

        // Arenan är sidans kartcentrum när det finns; API:ets position är reserven.
        var page = await _page.FetchAsync(key.EventId, cancellationToken);
        var arena = page.Arena
            ?? (competition.Location != default
                ? (competition.Location.Latitude, competition.Location.Longitude)
                : ((double, double)?)null);
        if (arena is null)
        {
            _logger.LogWarning("Tävlingen {EventId} saknar arenakoordinat — beställningen släpps.", key.EventId);
            return null;
        }

        var geometry = ArenaGeometry.From(arena.Value, page.Area);
        var when = ArenaImageKey.RenderTimeOf(competition);
        var (altitude, azimuth) = Sun.PositionOf(arena.Value.Item1, arena.Value.Item2, when);
        var light = Lighting.For(altitude, azimuth, key.Night);

        var scene = await _composer.ComposeBareAsync(geometry, key.Season, light, cancellationToken)
            ?? throw new InvalidOperationException(
                "höjdmodellen gick inte att hämta — beställningen går tillbaka på kön");

        var prompt = EnhancementPrompt.Compose(
            competition.Name, competition.District, key.Season, light, when,
            lamp: light.Night, wall: geometry.HasOutline);
        var enhancedPng = await model.EnhanceAsync(scene.Image.ToPng(), prompt, cancellationToken);

        var enhanced = ColorGridImage.Decode(enhancedPng);
        if (enhanced.Width != ArenaComposer.Width || enhanced.Height != ArenaComposer.Height)
        {
            _logger.LogInformation("Bildmodellen svarade {Width}x{Height}, skalar till {TargetWidth}x{TargetHeight}.",
                enhanced.Width, enhanced.Height, ArenaComposer.Width, ArenaComposer.Height);
            enhanced = enhanced.Resized(ArenaComposer.Width, ArenaComposer.Height);
        }

        // Murkontrollen före cachning: en bild där gränsen flyttats eller försvunnit ljuger
        // om arrangörens gränsdragning, och får hellre försökas om än sparas.
        if (scene.WallQuads is { } quads && !WallCheck.Survived(enhanced, quads, out var coverage))
            throw new InvalidOperationException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"murkontrollen underkände bilden: {coverage:P0} av muren kvar, kravet är {WallCheck.RequiredCoverage:P0}"));
        if (scene.WallQuads is not null)
            _logger.LogInformation("Murkontrollen godkände bilden.");

        return ArenaComposer.ApplyOverlays(scene, enhanced).ToPng();
    }
}
