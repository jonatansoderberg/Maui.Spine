using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Voice;

/// <summary>A pipeline's calls to the app's server: a turn written down, and an answer's sentence streamed as speech.</summary>
internal sealed class PipelineServer(SpineVoiceOptions options, Func<CancellationToken, Task<string?>> installationId)
{
    private const string SampleRateHeader = "Spine-Sample-Rate";

    public Task<string?> InstallationIdAsync(CancellationToken cancellationToken) => installationId(cancellationToken);

    /// <summary>The session's conversation, sent with every request.</summary>
    public string? ConversationId { get; set; }
    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

    private readonly Lazy<HttpClient> _http = options.HttpHandler is { } handler
        ? new(() => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) })
        : Shared;

    public async Task<string> TranscribeAsync(string feature, byte[] wav, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(wav);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var request = await RequestAsync("transcribe", feature, content, cancellationToken);
        using var response = await _http.Value.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Refusal(feature, request.RequestUri!, response, body);

        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("text", out var text) ? text.GetString()?.Trim() ?? "" : "";
    }

    public async IAsyncEnumerable<SpeechAudio> StreamSpeechAsync(string feature, string text, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = await RequestAsync("speech", feature,
            new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"), cancellationToken);
        request.Headers.Accept.ParseAdd("audio/pcm");
        using var response = await _http.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Refusal(feature, request.RequestUri!, response, await response.Content.ReadAsStringAsync(cancellationToken));
        if (response.Content.Headers.ContentType?.MediaType != "audio/pcm"
            || !response.Headers.TryGetValues(SampleRateHeader, out var rates) || !int.TryParse(rates.FirstOrDefault(), out var rate))
            throw new HttpRequestException($"The speech feature '{feature}' answered {response.Content.Headers.ContentType} without {SampleRateHeader}; the server needs Spine.Server.AI with streamed speech (Accept: audio/pcm).");

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[9600];
        var held = 0;
        int read;
        while ((read = await body.ReadAsync(buffer.AsMemory(held), cancellationToken)) > 0)
        {
            held += read;
            // Whole samples only; an odd byte waits for its other half.
            var whole = held & ~1;
            if (whole == 0)
                continue;
            yield return new SpeechAudio(buffer.AsSpan(0, whole).ToArray(), rate);
            buffer.AsSpan(whole, held - whole).CopyTo(buffer);
            held -= whole;
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(string route, string feature, HttpContent content, CancellationToken cancellationToken)
    {
        var endpoint = options.SessionEndpoint
            ?? throw new InvalidOperationException("A voice pipeline with server parts needs SpineVoiceOptions.SessionEndpoint: the prefix given to MapSpineAI.");
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{endpoint.ToString().TrimEnd('/')}/{route}/{Uri.EscapeDataString(feature)}")) { Content = content };
        if (options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
            request.Headers.TryAddWithoutValidation("Authorization", header);
        if (await installationId(cancellationToken) is { Length: > 0 } installation)
            request.Headers.TryAddWithoutValidation(SpineInstallation.Header, installation);
        foreach (var (name, value) in SpineClientHeaders.ForDevice(ConversationId))
            request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    private static Exception Refusal(string feature, Uri url, HttpResponseMessage response, string body)
    {
        AIOffReason? off = null;
        try
        {
            using var json = body.Length > 0 ? JsonDocument.Parse(body) : null;
            if (json?.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("off", out var o) && o.ValueKind == JsonValueKind.String)
                off = AIOffReasons.FromJson(o.GetString());
        }
        catch (JsonException)
        {
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Forbidden when off is { } reason => new AIFeatureOffException(feature, reason),
            HttpStatusCode.TooManyRequests => new AIRateLimitedException(feature, response.Headers.RetryAfter?.Delta),
            // A 404 without "off" is a route that does not exist: a wrong SessionEndpoint, or a server without MapSpineAI.
            _ => new HttpRequestException(
                $"The feature '{feature}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {(body.Length <= 300 ? body : body[..300] + "…")}",
                null, response.StatusCode),
        };
    }
}
