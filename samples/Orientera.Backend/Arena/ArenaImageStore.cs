using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.Arena;

/// <summary>
/// Arkivet av färdiga arenabilder. Backend äger uppslaget, inte skapandet.
/// </summary>
/// <remarks>
/// Genereringen tar en dryg minut — höjddata ska hämtas, terrängen renderas och bilden gå
/// genom en bildmodell — och hör därför inte hemma i en HTTP-väg som en telefon väntar på.
/// Den sker utanför, av en arbetare som skriver in i samma behållare. Det här är läsdelen:
/// finns bilden serveras den, annars sägs det rakt ut att den inte finns än.
///
/// Attributionen följer med varje svar. Bilden bär ingen text, så det är enda vägen den kan
/// nå fram till den som ser den.
/// </remarks>
public sealed class ArenaImageStore(
    IOptions<ArenaImageOptions> _options,
    ResponseCache _cache,
    ILogger<ArenaImageStore> _logger)
{
    private const string Credit =
        "Ortofoto och höjddata © Lantmäteriet (CC BY 4.0). Bilden är efterbehandlad.";

    private const string GenericCredit =
        "Illustration. Föreställer ingen viss lokal.";

    public async Task<ArenaImage?> FindAsync(Competition competition, CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!options.IsConfigured)
            return null;

        var key = ArenaImageKey.For(competition, options.Version);

        // Uppslaget cachas, inte bilden. Det som ändrar sig är inte innehållet utan huruvida
        // bilden hunnit bli till, och det är därför livslängden är kort.
        return await _cache.GetOrAddAsync(
            $"arenabild:{key.BlobName}",
            TimeSpan.FromMinutes(options.LookupMinutes),
            token => LookUpAsync(key, options, token),
            cancellationToken);
    }

    /// <summary>
    /// Beställer bilden av arbetaren som kan göra den.
    /// </summary>
    /// <remarks>
    /// Beställningen dubbleras med flit inte: uppslaget ligger redan bakom
    /// <see cref="ResponseCache"/>, så tusen telefoner som öppnar samma tävling samma minut
    /// ger ett meddelande, inte tusen. En misslyckad beställning får inte fälla uppslaget —
    /// att bilden saknas är fortfarande ett giltigt svar.
    /// </remarks>
    private async Task OrderAsync(
        ArenaImageKey key,
        ArenaImageOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var queue = new QueueClient(options.ConnectionString, options.Queue,
                new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

            await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            await queue.SendMessageAsync(
                System.Text.Json.JsonSerializer.Serialize(key), cancellationToken);

            _logger.LogInformation("Arenabild beställd ({Blob}).", key.BlobName);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogWarning(exception, "Arenabilden kunde inte beställas ({Blob}).", key.BlobName);
        }
    }

    private async Task<ArenaImage?> LookUpAsync(
        ArenaImageKey key,
        ArenaImageOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var container = new BlobContainerClient(options.ConnectionString, options.Container);
            var blob = container.GetBlobClient(key.BlobName);

            Azure.Response<Azure.Storage.Blobs.Models.BlobProperties> properties;
            try
            {
                properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                await OrderAsync(key, options, cancellationToken);

                return null;
            }

            var generic = key.Season == ArenaSeason.Inomhus;

            return new ArenaImage
            {
                // Ändringstiden i urlen: appen cachar bilden länge på enheten med urlen som
                // nyckel, och det är bara sant att innehållet aldrig ändras så länge en
                // omgjord blob under samma namn också blir en ny url. Utan detta serverade
                // telefonen en gammal bild i halvåret efter en omgenerering.
                Url = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{blob.Uri}?v={properties.Value.LastModified.ToUnixTimeSeconds()}"),
                Season = key.Season,
                Night = key.Night,
                Attribution = generic ? GenericCredit : Credit,
                IsGeneric = generic,
            };
        }
        // Ett uteblivet svar visas som ingen bild alls, vilket ser likadant ut som "inte
        // genererad än". Utan den här raden skiljer ingenting de två åt i efterhand.
        catch (RequestFailedException exception)
        {
            _logger.LogWarning(exception, "Arenabilden kunde inte slås upp ({Blob}).", key.BlobName);

            return null;
        }
    }
}
