using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orientera.Backend.Configuration;
using Orientera.Backend.Upstream;

namespace Orientera.Backend.LiveResults;

/// <summary>
/// The HTTP boundary against LiveResults. No authentication — the service is public — but it
/// does not always ship valid JSON, so this is also where the payload is made parseable.
/// </summary>
public sealed class LiveResultsClient(
    HttpClient _http,
    IOptions<LiveResultsOptions> _options,
    ILogger<LiveResultsClient> _logger)
{
    private readonly LiveResultsOptions _settings = _options.Value;

    public async Task<JsonElement> GetAsync(
        string method,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>())
        {
            ["method"] = method,
        };

        var uri = new UriBuilder(new Uri(_settings.BaseAddress))
        {
            Query = string.Join('&', parameters
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")),
        }.Uri;

        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LiveResults {Method} svarade {Status}.", method, (int)response.StatusCode);
                throw new UpstreamUnavailableException($"LiveResults svarade {(int)response.StatusCode} på {method}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<JsonElement>(Repair(body));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new UpstreamUnavailableException($"LiveResults kunde inte nås på {method}.", exception);
        }
    }

    /// <summary>
    /// Competition names come through with raw tabs and newlines inside the string values,
    /// which is not valid JSON and which every strict parser rejects. Escaping them is the
    /// difference between a working calendar and none at all.
    /// </summary>
    public static string Repair(string body)
    {
        if (!body.Any(c => char.IsControl(c) && c is not ('\r' or '\n')) && !body.Contains('\n'))
            return body;

        var repaired = new StringBuilder(body.Length);
        bool inString = false;
        bool escaped = false;

        foreach (char c in body)
        {
            if (escaped)
            {
                repaired.Append(c);
                escaped = false;
                continue;
            }

            switch (c)
            {
                case '\\' when inString:
                    escaped = true;
                    repaired.Append(c);
                    break;

                case '"':
                    inString = !inString;
                    repaired.Append(c);
                    break;

                case '\t' when inString:
                    repaired.Append("\\t");
                    break;

                case '\n' when inString:
                    repaired.Append("\\n");
                    break;

                case '\r' when inString:
                    repaired.Append("\\r");
                    break;

                case var control when inString && char.IsControl(control):
                    repaired.Append(' ');
                    break;

                default:
                    repaired.Append(c);
                    break;
            }
        }

        return repaired.ToString();
    }
}
