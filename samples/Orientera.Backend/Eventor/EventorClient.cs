using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Configuration;

namespace Orientera.Backend.Eventor;

/// <summary>
/// The HTTP boundary against Eventor. Everything above it sees XML documents or an
/// <see cref="EventorUnavailableException"/> — never a status code, never a socket.
/// </summary>
public sealed class EventorClient(
    HttpClient _http,
    IOptions<EventorOptions> _options,
    ILogger<EventorClient> _logger)
{
    private readonly EventorOptions _settings = _options.Value;

    /// <summary>
    /// Eventor is a shared service for the whole federation, so the backend keeps its own
    /// concurrency low rather than letting the number of app users decide it.
    /// </summary>
    private static readonly SemaphoreSlim Throttle = new(4, 4);

    public async Task<XElement> GetAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
            throw new EventorUnavailableException("Ingen API-nyckel för Eventor är konfigurerad.");

        var uri = BuildUri(path, query);

        await Throttle.WaitAsync(cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("ApiKey", _settings.ApiKey);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Eventor {Path} svarade {Status}.", path, (int)response.StatusCode);
                throw new EventorUnavailableException($"Eventor svarade {(int)response.StatusCode} på {path}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            return document.Root
                ?? throw new EventorUnavailableException($"Eventor svarade med ett tomt dokument på {path}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            throw new EventorUnavailableException($"Eventor kunde inte nås på {path}.", exception);
        }
        finally
        {
            Throttle.Release();
        }
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var baseAddress = _settings.BaseAddress.EndsWith('/') ? _settings.BaseAddress : _settings.BaseAddress + "/";
        var builder = new UriBuilder(new Uri(new Uri(baseAddress), path.TrimStart('/')));

        if (query is { Count: > 0 })
        {
            var pairs = query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

            builder.Query = string.Join('&', pairs);
        }

        return builder.Uri;
    }
}
