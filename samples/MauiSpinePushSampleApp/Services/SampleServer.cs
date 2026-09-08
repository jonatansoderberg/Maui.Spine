using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSpinePushSampleApp.Services;

/// <summary>What the sample server answered a send with.</summary>
/// <param name="Sent">How many the services accepted.</param>
/// <param name="Invalid">How many had a dead token.</param>
/// <param name="Throttled">How many were throttled.</param>
/// <param name="Failed">How many failed otherwise.</param>
/// <param name="Note">Why nothing was sent, when that is the case.</param>
public sealed record SendResult(int Sent, int Invalid, int Throttled, int Failed, string? Note)
{
    /// <summary>The one-line summary the Send page shows.</summary>
    public override string ToString() =>
        Note ?? $"sent {Sent}, invalid {Invalid}, throttled {Throttled}, failed {Failed}";
}

/// <summary>
/// The sample server's <c>POST /send</c>. It exists so a developer can push to their own device from
/// the app itself, without Postman and without a real backend.
/// </summary>
/// <param name="endpoint">Where the server is.</param>
public sealed class SampleServer(Uri endpoint)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Asks the server to send one message.</summary>
    /// <param name="request">What to send and to whom.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the server answered, or a line explaining why it could not be reached.</returns>
    public async Task<string> SendAsync(object request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(endpoint, request, Json, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return $"the server answered {(int)response.StatusCode}";

            var result = await response.Content.ReadFromJsonAsync<SendResult>(Json, cancellationToken);
            return result?.ToString() ?? "the server answered nothing";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return $"could not reach {endpoint}: {e.Message}";
        }
    }
}
