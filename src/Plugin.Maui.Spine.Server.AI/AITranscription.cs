using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>What the server asks a transcription provider to write down.</summary>
/// <param name="Feature">The feature's name.</param>
/// <param name="Model">The model, such as <c>gpt-4o-transcribe</c>.</param>
/// <param name="Audio">A WAV file: PCM16, mono.</param>
/// <param name="Options">The feature's transcription settings.</param>
/// <param name="Caller">Who it is for.</param>
public sealed record AITranscriptionRequest(string Feature, string Model, ReadOnlyMemory<byte> Audio, AITranscriptionOptions Options, AICaller Caller);

/// <summary>What was said.</summary>
/// <param name="Text">The words, empty when nothing was heard.</param>
public sealed record AITranscription(string Text);

/// <summary>Writes speech down for <c>Kind: Transcription</c> features. Register one with <see cref="SpineAIOptions.Transcription"/>.</summary>
public interface IAITranscriptionProvider
{
    /// <summary>Writes down what <see cref="AITranscriptionRequest.Audio"/> says.</summary>
    /// <param name="request">The audio and the feature's settings.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The words.</returns>
    Task<AITranscription> TranscribeAsync(AITranscriptionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The built-in transcription providers.</summary>
public static class AITranscriptionProviders
{
    private static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// OpenAI's <c>POST /v1/audio/transcriptions</c> with <c>ApiKey</c> from the provider's section and an optional
    /// <c>Endpoint</c> (<c>https://api.openai.com/v1/</c> by default). Models such as <c>gpt-4o-transcribe</c>,
    /// <c>gpt-4o-mini-transcribe</c> or <c>whisper-1</c>.
    /// </summary>
    /// <param name="provider">The provider's section under <c>Ai:Providers</c>.</param>
    /// <returns>The provider.</returns>
    public static IAITranscriptionProvider OpenAI(IConfiguration provider) => OpenAI(provider, Shared);

    internal static IAITranscriptionProvider OpenAI(IConfiguration provider, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var key = provider["ApiKey"] is { Length: > 0 } apiKey ? apiKey : throw new InvalidOperationException("the provider has no ApiKey.");
        var root = new Uri((provider["Endpoint"] is { Length: > 0 } endpoint ? endpoint : "https://api.openai.com/v1").TrimEnd('/') + "/");
        return new OpenAITranscriptionProvider(http, root, key);
    }

    /// <summary>How long a WAV file plays, from its header; zero when it is not one.</summary>
    /// <param name="wav">The file.</param>
    internal static TimeSpan DurationOf(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 44 || !wav[..4].SequenceEqual("RIFF"u8) || !wav.Slice(8, 4).SequenceEqual("WAVE"u8)) return TimeSpan.Zero;

        int channels = 0, rate = 0, bits = 0;
        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = wav.Slice(at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(at + 4, 4));
            if (size < 0) break;

            if (id.SequenceEqual("fmt "u8) && at + 24 <= wav.Length)
            {
                channels = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(at + 10, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(at + 12, 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(at + 22, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                var bytesPerSecond = (double)channels * rate * bits / 8;
                return bytesPerSecond > 0 ? TimeSpan.FromSeconds(Math.Min(size, wav.Length - at - 8) / bytesPerSecond) : TimeSpan.Zero;
            }

            at += 8 + size + (size & 1);
        }

        return TimeSpan.Zero;
    }
}

internal sealed class OpenAITranscriptionProvider(HttpClient http, Uri root, string key) : IAITranscriptionProvider
{
    public async Task<AITranscription> TranscribeAsync(AITranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var audio = new ByteArrayContent(request.Audio.ToArray());
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        using var form = new MultipartFormDataContent
        {
            { audio, "file", "speech.wav" },
            { new StringContent(request.Model), "model" },
            { new StringContent("json"), "response_format" },
        };
        if (request.Options.Language is { Length: > 0 } language) form.Add(new StringContent(LanguageOf(language)), "language");
        if (request.Options.Prompt is { Length: > 0 } prompt) form.Add(new StringContent(prompt), "prompt");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "audio/transcriptions")) { Content = form };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await http.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The provider's answer goes into the exception, which the endpoint logs and does not pass to the app.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} from {message.RequestUri}: {(text.Length <= 500 ? text : text[..500] + "…")}"),
                null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(text);
        return new AITranscription(json.RootElement.TryGetProperty("text", out var words) ? words.GetString()?.Trim() ?? "" : "");
    }

    // OpenAI takes ISO-639-1 ("sv"), not a full tag ("sv-SE").
    private static string LanguageOf(string tag) => tag.Split('-', 2)[0].ToLowerInvariant();
}
