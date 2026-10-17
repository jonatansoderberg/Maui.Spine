using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What the server asks a speech provider to read aloud.</summary>
/// <param name="Feature">The feature's name.</param>
/// <param name="Model">The model, or for Azure OpenAI the deployment.</param>
/// <param name="Text">The text, as the app sent it.</param>
/// <param name="Options">The feature's voice, language and style.</param>
/// <param name="Caller">Who it is for.</param>
public sealed record AISpeechRequest(string Feature, string Model, string Text, AISpeechOptions Options, AICaller Caller);

/// <summary>A mouth shape at a moment of the audio, in Azure Speech's alphabet of 22 (0 silence, 21 p, b, m).</summary>
/// <param name="Id">The viseme, 0–21.</param>
/// <param name="Offset">When it starts, from the start of the audio.</param>
public sealed record AISpeechViseme(int Id, TimeSpan Offset);

/// <summary>Text read aloud: PCM16, little-endian mono, and the mouth shapes when the provider gives them.</summary>
/// <param name="Pcm">The samples.</param>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="Visemes">The mouth shapes in time order; empty when the provider has none (OpenAI).</param>
public sealed record AISpeech(ReadOnlyMemory<byte> Pcm, int SampleRate, IReadOnlyList<AISpeechViseme> Visemes)
{
    /// <summary>How long the audio plays.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Pcm.Length / 2.0 / SampleRate);
}

/// <summary>Reads text aloud with one provider. Register one with <see cref="SpineAIOptions.Speech"/>.</summary>
/// <remarks>Microsoft.Extensions.AI has no text-to-speech abstraction yet, so this is Spine's own.</remarks>
public interface IAISpeechProvider
{
    /// <summary>Reads <see cref="AISpeechRequest.Text"/> aloud.</summary>
    /// <param name="request">The text and the feature's voice.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The audio and its mouth shapes.</returns>
    Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The built-in speech providers.</summary>
public static class AISpeechProviders
{
    private static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// OpenAI's <c>POST /v1/audio/speech</c> with <c>ApiKey</c> from the provider's section and an optional <c>Endpoint</c>
    /// (<c>https://api.openai.com/v1/</c> by default). Audio only: the app estimates the mouth shapes from the text.
    /// </summary>
    /// <param name="provider">The provider's section under <c>Ai:Providers</c>.</param>
    /// <returns>The provider.</returns>
    public static IAISpeechProvider OpenAI(IConfiguration provider) => OpenAI(provider, Shared);

    /// <summary>
    /// Azure Speech, with mouth shapes: the service's WebSocket (the protocol of Microsoft's open-source JavaScript Speech
    /// SDK) at <c>wss://{Region}.tts.speech.microsoft.com</c>, or <c>Endpoint</c> when set, with <c>ApiKey</c> from the
    /// provider's section. The feature's <c>Speech:Voice</c> is the neural voice, such as <c>sv-SE-SofieNeural</c>; its
    /// <c>Model</c> is recorded in the usage only.
    /// </summary>
    /// <param name="provider">The provider's section under <c>Ai:Providers</c>: <c>Region</c> (<c>swedencentral</c>) or <c>Endpoint</c>, and <c>ApiKey</c>.</param>
    /// <returns>The provider.</returns>
    public static IAISpeechProvider AzureSpeech(IConfiguration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var key = provider["ApiKey"] is { Length: > 0 } apiKey ? apiKey : throw new InvalidOperationException("the provider has no ApiKey.");
        var endpoint = provider["Endpoint"] is { Length: > 0 } custom
            ? new Uri(custom)
            : provider["Region"] is { Length: > 0 } region
                ? new Uri($"wss://{region}.tts.speech.microsoft.com/tts/cognitiveservices/websocket/v1")
                : throw new InvalidOperationException("Azure Speech needs Region, such as swedencentral, or Endpoint.");
        return new AzureSpeechProvider(endpoint, key);
    }

    internal static IAISpeechProvider OpenAI(IConfiguration provider, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var endpoint = provider["Endpoint"] is { Length: > 0 } custom ? custom : "https://api.openai.com/v1/";
        var key = provider["ApiKey"] is { Length: > 0 } apiKey ? apiKey : throw new InvalidOperationException("the provider has no ApiKey.");
        return new OpenAISpeechProvider(http, new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/"), key);
    }
}

internal sealed class OpenAISpeechProvider(HttpClient http, Uri root, string key) : IAISpeechProvider
{
    // OpenAI's raw PCM is 24 kHz, 16-bit, mono.
    private const int Rate = 24_000;

    public async Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "audio/speech"))
        {
            Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The provider's answer goes into the exception, which the endpoint logs and does not pass to the app.
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} from {message.RequestUri}: {(text.Length <= 500 ? text : text[..500] + "…")}"),
                null, response.StatusCode);
        }

        var pcm = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new AISpeech(pcm, Rate, []);
    }

    internal static JsonObject Body(AISpeechRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["input"] = request.Text,
            ["voice"] = request.Options.Voice is { Length: > 0 } voice ? voice : "alloy",
            ["response_format"] = "pcm",
        };
        if (request.Options.Instructions is { Length: > 0 } instructions) body["instructions"] = instructions;
        if (request.Options.Speed is { } speed) body["speed"] = speed;
        return body;
    }
}

/// <summary>
/// Azure Speech over its WebSocket, which is how its SDKs get visemes; the REST endpoint returns audio only. Messages are
/// text (headers, a blank line, the body) or binary (a big-endian header length, the headers, the audio), each with a
/// <c>Path</c>; one request is <c>speech.config</c>, <c>synthesis.context</c>, <c>ssml</c>, answered by <c>turn.start</c>,
/// <c>audio</c> and <c>audio.metadata</c> until <c>turn.end</c>.
/// </summary>
internal sealed class AzureSpeechProvider(Uri endpoint, string key, Func<ClientWebSocket>? sockets = null) : IAISpeechProvider
{
    internal const int Rate = 24_000;
    internal const string Format = "raw-24khz-16bit-mono-pcm";
    private const string DefaultVoice = "en-US-AvaMultilingualNeural";

    public async Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var socket = sockets?.Invoke() ?? new ClientWebSocket();
        var connection = Guid.NewGuid().ToString("N");
        socket.Options.SetRequestHeader("Ocp-Apim-Subscription-Key", key);
        socket.Options.SetRequestHeader("X-ConnectionId", connection);
        await socket.ConnectAsync(new UriBuilder(endpoint) { Query = "X-ConnectionId=" + connection }.Uri, cancellationToken);

        var id = Guid.NewGuid().ToString("N");
        await SendAsync(socket, "speech.config", id, "application/json", SpeechConfig, cancellationToken);
        await SendAsync(socket, "synthesis.context", id, "application/json", SynthesisContext(), cancellationToken);
        await SendAsync(socket, "ssml", id, "application/ssml+xml", Ssml(request), cancellationToken);

        using var audio = new MemoryStream();
        var visemes = new List<AISpeechViseme>();
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            buffer.SetLength(0);
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(chunk, cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                    throw new HttpRequestException($"Azure Speech closed the connection before the end: {socket.CloseStatus} {socket.CloseStatusDescription}");
                buffer.Write(chunk, 0, received.Count);
            }
            while (!received.EndOfMessage);

            var message = buffer.ToArray();
            if (received.MessageType == WebSocketMessageType.Binary)
            {
                var (headers, body) = ReadBinary(message);
                if (Path(headers) == "audio")
                    audio.Write(body.Span);
                continue;
            }

            var (textHeaders, text) = ReadText(Encoding.UTF8.GetString(message));
            switch (Path(textHeaders))
            {
                case "audio.metadata":
                    ReadVisemes(text, visemes);
                    break;
                case "turn.end":
                    await CloseAsync(socket);
                    visemes.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                    return new AISpeech(audio.ToArray(), Rate, visemes);
            }
        }
    }

    internal const string SpeechConfig = """{"context":{"system":{"name":"Plugin.Maui.Spine.Server.AI","build":".NET"}}}""";

    internal static string SynthesisContext() => new JsonObject
    {
        ["synthesis"] = new JsonObject
        {
            ["audio"] = new JsonObject
            {
                ["metadataOptions"] = new JsonObject
                {
                    ["visemeEnabled"] = true,
                    ["wordBoundaryEnabled"] = false,
                    ["sentenceBoundaryEnabled"] = false,
                    ["bookmarkEnabled"] = false,
                    ["sessionEndEnabled"] = true,
                },
                ["outputFormat"] = Format,
            },
            ["language"] = new JsonObject { ["autoDetection"] = false },
        },
    }.ToJsonString();

    internal static string Ssml(AISpeechRequest request)
    {
        var voice = request.Options.Voice is { Length: > 0 } named ? named : DefaultVoice;
        // A neural voice's name starts with its language, which SSML needs when the feature names none.
        var language = request.Options.Language is { Length: > 0 } set ? set : string.Join('-', voice.Split('-').Take(2));
        var text = SecurityElement.Escape(request.Text);
        var rate = request.Options.Speed is { } speed ? string.Create(CultureInfo.InvariantCulture, $"<prosody rate='{speed:0.##}'>{text}</prosody>") : text;
        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{SecurityElement.Escape(language)}'>"
            + $"<voice name='{SecurityElement.Escape(voice)}'>{rate}</voice></speak>";
    }

    internal static void ReadVisemes(string json, List<AISpeechViseme> into)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Metadata", out var list) || list.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in list.EnumerateArray())
        {
            if (item.TryGetProperty("Type", out var type) && type.GetString() == "Viseme"
                && item.TryGetProperty("Data", out var data)
                && data.TryGetProperty("Offset", out var offset) && data.TryGetProperty("VisemeId", out var viseme))
            {
                // Offsets are in 100-nanosecond ticks from the start of the audio.
                into.Add(new AISpeechViseme(viseme.GetInt32(), TimeSpan.FromTicks(offset.GetInt64())));
            }
        }
    }

    internal static (Dictionary<string, string> Headers, string Body) ReadText(string message)
    {
        var split = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return split < 0 ? (Headers(message), "") : (Headers(message[..split]), message[(split + 4)..]);
    }

    internal static (Dictionary<string, string> Headers, ReadOnlyMemory<byte> Body) ReadBinary(byte[] message)
    {
        if (message.Length < 2)
            return ([], ReadOnlyMemory<byte>.Empty);
        var length = BinaryPrimitives.ReadInt16BigEndian(message);
        var headers = Headers(Encoding.ASCII.GetString(message, 2, Math.Min(length, message.Length - 2)));
        return (headers, message.AsMemory(Math.Min(2 + length, message.Length)));
    }

    private static Dictionary<string, string> Headers(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    private static string Path(Dictionary<string, string> headers) =>
        headers.TryGetValue("Path", out var path) ? path.ToLowerInvariant() : "";

    private static Task SendAsync(ClientWebSocket socket, string path, string id, string contentType, string body, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var message = $"Path: {path}\r\nX-RequestId: {id}\r\nX-Timestamp: {timestamp}\r\nContent-Type: {contentType}\r\n\r\n{body}";
        return socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task CloseAsync(ClientWebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // The answer is complete; a close that does not go through changes nothing.
        }
    }
}
