using Orientera.Domain.Ranking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;

namespace Orientera.Backend.Ranking;

/// <summary>Fetches one club's Sverigelistan page and reads it.</summary>
public sealed class RankingScraper(
    HttpClient _http,
    ResponseCache _cache,
    IOptions<RankingOptions> _options,
    ILogger<RankingScraper> _logger)
{
    public Task<IReadOnlyList<RankingRow>> ForClubAsync(string clubId, CancellationToken cancellationToken) =>
        _cache.GetOrAddAsync(
            $"ranking:{clubId}",
            TimeSpan.FromHours(_options.Value.CacheHours),
            token => FetchAsync(clubId, token),
            cancellationToken);

    private async Task<IReadOnlyList<RankingRow>> FetchAsync(string clubId, CancellationToken cancellationToken)
    {
        try
        {
            // Club-wise lists exist for forest only; sprint is published nationally, per class,
            // and fetching those would be the sweep this was meant not to be (SP-02).
            using var response = await _http.GetAsync($"Ranking/ol/Club/Index/{clubId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            return RankingPageParser.Parse(clubId, html);
        }
        // One club that will not load is one club, not a failed run.
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Rankingsidan för klubb {Club} kunde inte hämtas.", clubId);

            return [];
        }
    }
}
