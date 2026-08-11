using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.Livelox;

/// <summary>
/// Livelox for a competition the app already knows from Eventor.
/// </summary>
/// <remarks>
/// The matching problem the spike expected — date, position and name, the way LiveResults has to
/// be matched (SP-04) — does not exist here. Livelox addresses Swedish events by the Eventor id
/// itself: <c>EventorSweden:{eventId}-{race}</c>. There is nothing to guess and nothing to get
/// wrong, which is why this source has no matcher.
///
/// What it cannot do is fetch courses. That endpoint exists but is scoped, and the key we have
/// does not carry <c>courses.read</c>. Maps and routes have no endpoint at all — Livelox keeps
/// them, deliberately. So this is a link, honestly labelled, and not a data source.
/// </remarks>
public sealed class LiveloxSource(
    HttpClient _http,
    ResponseCache _cache,
    IOptions<LiveloxOptions> _options,
    ILogger<LiveloxSource> _logger)
{
    /// <summary>
    /// Single-stage competitions are race 1, which is nearly all of them. A multi-stage event
    /// would need the stage number, and Eventor's calendar does not carry one — so the first
    /// race is what is offered rather than a guess at which stage the runner means.
    /// </summary>
    private const int FirstRace = 1;

    public Task<LiveloxLink?> GetAsync(CompetitionId competition, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"livelox:{competition.Value}",
            TimeSpan.FromHours(_options.Value.CacheHours),
            token => FetchAsync(competition, token),
            cancellationToken);

    private async Task<LiveloxLink?> FetchAsync(CompetitionId competition, CancellationToken cancellationToken)
    {
        // The colon is part of the id, not a path separator, so it has to survive as %3A.
        var id = Uri.EscapeDataString($"EventorSweden:{competition.Value}-{FirstRace}");

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"events/{id}?includeClasses=true&includeUrls=true");

        if (_options.Value.ApiKey is { Length: > 0 } key)
            request.Headers.Add("ApiKey", key);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);

            // 404 is Livelox's answer for "we have never heard of this competition", which is a
            // fact about the competition rather than a failure of the call.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

            return Read(await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Livelox is a courtesy link, not something the competition page depends on. A page
            // that fails to load because an optional link could not be checked is worse than a
            // page without the link.
            _logger.LogWarning(exception, "Livelox svarade inte för {Competition}.", competition.Value);

            return null;
        }
    }

    private static LiveloxLink? Read(JsonDocument document)
    {
        var root = document.RootElement;

        if (Text(root, "url") is not { } url || Text(root, "name") is not { } name)
            return null;

        var classes = root.TryGetProperty("classes", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(c => Text(c, "name") is not null && Text(c, "url") is not null)
                .Select(c => new LiveloxClass { Name = Text(c, "name")!, Url = Text(c, "url")! })
                .ToList()
            : [];

        return new LiveloxLink
        {
            Name = name,
            Url = url,
            HasMap = root.TryGetProperty("hasValidMapAndCourses", out var map) && map.ValueKind == JsonValueKind.True,
            Participants = root.TryGetProperty("participantCount", out var count) && count.TryGetInt32(out int n) ? n : 0,
            Classes = classes,
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
