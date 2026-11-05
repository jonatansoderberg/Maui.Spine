using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSpineSampleApp.Push;

/// <summary>What the Showcase server answered a send with.</summary>
/// <param name="Sent">How many the services accepted.</param>
/// <param name="Invalid">How many had a dead token.</param>
/// <param name="Throttled">How many were throttled.</param>
/// <param name="Failed">How many failed otherwise.</param>
/// <param name="Note">Why nothing was sent, when that is the case.</param>
public sealed record SendResult(int Sent, int Invalid, int Throttled, int Failed, string? Note)
{
    /// <summary>The one-line summary the pages show.</summary>
    public override string ToString() =>
        Note ?? $"sent {Sent}, invalid {Invalid}, throttled {Throttled}, failed {Failed}";
}

/// <summary>
/// The Showcase server's <c>POST /send</c> and <c>POST /channels</c>, so a developer can push to their own device from the
/// app itself, without Postman and without a backend of their own.
/// </summary>
/// <remarks>
/// Through the Sign-in page's client, with the access token: in Azure the server sends only to the signed-in user's own
/// devices, and answers 401 to anyone else. On the Mac it sends to whoever asks.
/// </remarks>
/// <param name="clients">Makes the <c>SampleApi</c> client.</param>
public sealed class PushServer(IHttpClientFactory clients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string SignInFirst = "sign in first (the Sign-in page): the server in Azure sends only to your own devices";

    /// <summary>Asks the server to send one message.</summary>
    /// <param name="request">What to send and to whom.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the server answered, or a line explaining why it could not be reached.</returns>
    public async Task<string> SendAsync(object request, CancellationToken cancellationToken = default)
    {
        using var client = Client();
        try
        {
            using var response = await client.PostAsJsonAsync("send", request, Json, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized) return SignInFirst;
            if (!response.IsSuccessStatusCode)
                return $"the server answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";

            var result = await response.Content.ReadFromJsonAsync<SendResult>(Json, cancellationToken);
            return result?.ToString() ?? "the server answered nothing";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return $"could not reach {client.BaseAddress}: {e.Message}";
        }
    }

    /// <summary>Asks the server for its broadcast channel, which it creates at APNs the first time.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The channel id, or why there is none.</returns>
    public async Task<(string? Channel, string? Error)> ChannelAsync(CancellationToken cancellationToken = default)
    {
        using var client = Client();
        try
        {
            using var response = await client.PostAsync("channels", content: null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return (null, SignInFirst);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, $"the server answered {(int)response.StatusCode}: {body}");

            return JsonDocument.Parse(body).RootElement.TryGetProperty("channel", out var channel) && channel.GetString() is { Length: > 0 } id
                ? (id, null)
                : (null, $"the server answered without a channel: {body}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (null, $"could not reach {client.BaseAddress}: {e.Message}");
        }
    }

    private HttpClient Client()
    {
        var client = clients.CreateClient("SampleApi");
        // The server in Azure sleeps on its free plan, and the first call after a pause waits for it to wake.
        client.Timeout = TimeSpan.FromSeconds(75);
        return client;
    }
}
