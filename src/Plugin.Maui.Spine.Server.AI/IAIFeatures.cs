using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>Whether a feature answers.</summary>
public enum AIFeatureState
{
    /// <summary>It answers.</summary>
    On,

    /// <summary>No such feature, or its provider, key or model is missing. The startup log says which.</summary>
    NotConfigured,

    /// <summary><c>Enabled</c> is <see langword="false"/>.</summary>
    Disabled,

    /// <summary>The app's or the feature's hard budget is spent for the rest of the day or month.</summary>
    BudgetSpent,
}

/// <summary>The named features, for the server's own endpoints.</summary>
/// <example>
/// <code>
/// var caller = await ai.IdentifyAsync(request);
/// if (ai.Chat("RaceStory", caller) is not { } story) return Results.NotFound();
/// try
/// {
///     var answer = await story.GetResponseAsync(prompt, cancellationToken: ct);
///     return Results.Ok(answer.Text);
/// }
/// catch (AIFeatureException e) { return e.ToResult(); }
/// </code>
/// </example>
public interface IAIFeatures
{
    /// <summary>
    /// The feature's client for <paramref name="caller"/>, metered and held to the budgets and limits, or
    /// <see langword="null"/> when the feature is off or makes pictures.
    /// </summary>
    /// <param name="feature">The feature's name.</param>
    /// <param name="caller">Who the call is for; <see cref="AICaller.System"/> for the server's own work.</param>
    /// <remarks>
    /// A budget can still run out between this and the call, or the caller can be over a limit. The call then throws
    /// an <see cref="AIFeatureException"/>, and <see cref="AIFeatureResults.ToResult"/> is the answer to return.
    /// </remarks>
    IChatClient? Chat(string feature, AICaller caller);

    /// <summary>The image counterpart of <see cref="Chat"/>.</summary>
    /// <param name="feature">The feature's name.</param>
    /// <param name="caller">Who the call is for.</param>
    IImageGenerator? Image(string feature, AICaller caller);

    /// <summary>
    /// Sets up a realtime voice session for <paramref name="caller"/>: checks the feature's callers, rates and budgets,
    /// asks the provider for a short-lived token (Live: issues a ticket for the server's relay) and reserves the
    /// session's minutes.
    /// </summary>
    /// <param name="feature">The feature's name; its <c>Kind</c> is <c>Realtime</c>.</param>
    /// <param name="caller">Who the session is for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="voice">
    /// The voice the app asks for, such as an avatar's (<c>AvatarManifest.Voices</c>): used when it is one of the feature's
    /// <see cref="AIRealtimeOptions.Voices"/>, otherwise the feature's own <see cref="AIRealtimeOptions.Voice"/>.
    /// </param>
    /// <param name="maxMinutes">
    /// A shorter limit for this session than the feature's <see cref="AIRealtimeOptions.MaxMinutes"/>, to bound its cost;
    /// the reservation follows it. <see langword="null"/>, or more than the feature allows, gives the feature's.
    /// </param>
    /// <returns>
    /// What the app connects with, or <see langword="null"/> when the feature is off or not a realtime feature. A Live
    /// session's <see cref="AIRealtimeSession.Url"/> is relative to the prefix given to <c>MapSpineRealtimeSessions</c>.
    /// </returns>
    /// <exception cref="AIFeatureException">The caller is refused, over a rate or out of budget.</exception>
    Task<AIRealtimeSession?> RealtimeAsync(
        string feature, AICaller caller, CancellationToken cancellationToken = default, string? voice = null, double? maxMinutes = null);

    /// <summary>
    /// Reads <paramref name="text"/> aloud for <paramref name="caller"/>: checks the feature's callers, rates and budgets,
    /// asks the provider and records the characters.
    /// </summary>
    /// <param name="feature">The feature's name; its <c>Kind</c> is <c>Speech</c>.</param>
    /// <param name="text">The text; at most the feature's <see cref="AISpeechOptions.MaxCharacters"/>.</param>
    /// <param name="caller">Who it is for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The audio and its mouth shapes, or <see langword="null"/> when the feature is off or not a speech feature.</returns>
    /// <exception cref="AIFeatureException">The caller is refused, over a rate or out of budget, or the text is too long.</exception>
    Task<AISpeech?> SpeechAsync(string feature, string text, AICaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes down what <paramref name="wav"/> says for <paramref name="caller"/>: checks the feature's callers, rates and
    /// budgets, asks the provider and records the audio's minutes.
    /// </summary>
    /// <param name="feature">The feature's name; its <c>Kind</c> is <c>Transcription</c>.</param>
    /// <param name="wav">A WAV file, PCM16 mono; at most the feature's <see cref="AITranscriptionOptions.MaxSeconds"/>.</param>
    /// <param name="caller">Who it is for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The words, or <see langword="null"/> when the feature is off or not a transcription feature.</returns>
    /// <exception cref="AIFeatureException">The caller is refused, over a rate or out of budget, or the audio is too long.</exception>
    Task<AITranscription?> TranscribeAsync(string feature, ReadOnlyMemory<byte> wav, AICaller caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <paramref name="text"/> aloud for <paramref name="caller"/> as the audio is made: a provider that streams
    /// (<see cref="IAIStreamingSpeechProvider"/>) hands over each piece as it comes, any other its whole audio as one piece.
    /// The characters are recorded when the stream ends.
    /// </summary>
    /// <param name="feature">The feature's name; its <c>Kind</c> is <c>Speech</c>.</param>
    /// <param name="text">The text; at most the feature's <see cref="AISpeechOptions.MaxCharacters"/>.</param>
    /// <param name="caller">Who it is for.</param>
    /// <param name="cancellationToken">Cancels the call; a reader that stops ends the stream.</param>
    /// <returns>The sample rate and the PCM16 pieces, or <see langword="null"/> when the feature is off or not a speech feature.</returns>
    /// <exception cref="AIFeatureException">The caller is refused, over a rate or out of budget, or the text is too long.</exception>
    Task<AISpeechStream?> StreamSpeechAsync(string feature, string text, AICaller caller, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="feature"/> answers right now.</summary>
    /// <param name="feature">The feature's name.</param>
    AIFeatureState StateOf(string feature);

    /// <summary>Who <paramref name="request"/> is for, by <see cref="SpineAIOptions.Identify"/>.</summary>
    /// <param name="request">The incoming request.</param>
    ValueTask<AICaller> IdentifyAsync(HttpRequest request);
}

internal sealed class AIFeatures(
    AIFeatureRegistry registry, AIBudgetGate gate, AIUsageMeter meter, SpineAIOptions options, AIPendingCalls pending, IServiceProvider services) : IAIFeatures
{
    public IChatClient? Chat(string feature, AICaller caller)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Chat is not { } shared) return null;

        IChatClient client = new CallerChatClient(shared, found, caller, gate, meter);

        // Outside the gate and the meter, so every round of function calls is checked and metered on its own.
        if (found.Options.Functions is { Count: > 0 })
        {
            return new FeatureFunctionsChatClient(client, found, caller, options.Functions, pending, services,
                services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance);
        }

        // Outside the gate and the meter: a cached answer costs nothing and is served even when the budget is spent.
        // Not with functions: their answers depend on the caller, and the cache key does not.
        if (found.Options.Cache?.Ttl is { } ttl && services.GetService<IDistributedCache>() is { } cache)
        {
            client = new ExpiringCachingChatClient(client, cache, ttl)
            {
                CacheKeyAdditionalValues = [found.Name, found.Model],
            };
        }

        return client;
    }

    public IImageGenerator? Image(string feature, AICaller caller)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        return TryGetOn(feature, out var found) && found.Image is { } shared
            ? new CallerImageGenerator(shared, found, caller, gate, meter)
            : null;
    }

    public async Task<AIRealtimeSession?> RealtimeAsync(
        string feature, AICaller caller, CancellationToken cancellationToken = default, string? voice = null, double? maxMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Realtime is not { } provider) return null;

        using var lease = gate.Acquire(found, caller);
        await gate.CheckAsync(found, caller, cancellationToken);

        var session = (found.Options.Realtime ?? new AIRealtimeOptions()).WithVoice(voice);
        var limit = session.LimitFor(maxMinutes);
        var request = new AIRealtimeRequest(found.Name, found.Model, found.Options.SystemPrompt, found.Options.MaxOutputTokens, session, caller)
        {
            Tools = FunctionsOf(found),
            MaxMinutes = limit,
        };
        var started = meter.Time.GetTimestamp();

        try
        {
            var made = session.Protocol == AIRealtimeProtocol.Live
                ? RelayTicket(found, caller, session, limit, AIRealtimeProviders.LiveProtocol)
                : session.Relay
                    ? RelayTicket(found, caller, session, limit, AIRealtimeProviders.RelayProtocol)
                    : await provider.CreateSessionAsync(request, cancellationToken);
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, null, minutes: (decimal)limit);
            // A session the app runs with the provider directly can report its usage afterwards; the ticket says what was reserved.
            var usageTicket = made.Protocol == AIRealtimeProviders.OpenAIProtocol
                ? services.GetRequiredService<AIRealtimeTickets>().Issue(found.Name, caller, limit, session.Voice, TimeSpan.FromMinutes(limit) + UsageReportGrace)
                : null;
            return made with { Voice = session.Voice, MaxMinutes = limit, UsageTicket = usageTicket };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, e);
            throw;
        }
    }

    // How long after its limit a direct session may still report its usage: a link kept warm reports when it finally closes.
    private static readonly TimeSpan UsageReportGrace = TimeSpan.FromHours(24);

    /// <summary>A ticket for the server's relay, which opens the provider's socket (Live, or the Realtime API) when the app connects with it.</summary>
    private AIRealtimeSession RelayTicket(AIFeature found, AICaller caller, AIRealtimeOptions session, double limit, string protocol)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(session.TokenSeconds, 10, 7200));
        var ticket = services.GetRequiredService<AIRealtimeTickets>().Issue(found.Name, caller, limit, session.Voice, lifetime);
        var url = $"realtime/{Uri.EscapeDataString(found.Name)}/relay?ticket={Uri.EscapeDataString(ticket)}";
        return new AIRealtimeSession(protocol, url, ticket, meter.Time.GetUtcNow() + lifetime);
    }

    public async Task<AISpeech?> SpeechAsync(string feature, string text, AICaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Speech is not { } provider) return null;

        var options = found.Options.Speech ?? new AISpeechOptions();
        if (text.Length > options.MaxCharacters)
            throw new AIRequestTooLargeException(found.Name, "characters", options.MaxCharacters);

        using var lease = gate.Acquire(found, caller);
        await gate.CheckAsync(found, caller, cancellationToken);

        var request = new AISpeechRequest(found.Name, found.Model, text, options, caller);
        var started = meter.Time.GetTimestamp();

        try
        {
            var speech = await provider.SynthesizeAsync(request, cancellationToken);
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, null, characters: text.Length);
            return speech;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, e);
            throw;
        }
    }

    public async Task<AITranscription?> TranscribeAsync(string feature, ReadOnlyMemory<byte> wav, AICaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Transcription is not { } provider) return null;

        var options = found.Options.Transcription ?? new AITranscriptionOptions();
        var duration = AITranscriptionProviders.DurationOf(wav.Span);
        if (duration.TotalSeconds > options.MaxSeconds)
            throw new AIRequestTooLargeException(found.Name, "seconds", options.MaxSeconds);

        using var lease = gate.Acquire(found, caller);
        await gate.CheckAsync(found, caller, cancellationToken);

        var request = new AITranscriptionRequest(found.Name, found.Model, wav, options, caller);
        var started = meter.Time.GetTimestamp();

        try
        {
            var heard = await provider.TranscribeAsync(request, cancellationToken);
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, null, minutes: (decimal)duration.TotalMinutes, measured: true);
            return heard;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, e);
            throw;
        }
    }

    public async Task<AISpeechStream?> StreamSpeechAsync(string feature, string text, AICaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Speech is not { } speech) return null;

        // A provider that does not stream makes its whole audio first; it is the one piece.
        if (speech is not IAIStreamingSpeechProvider provider)
            return await SpeechAsync(feature, text, caller, cancellationToken) is { } whole ? new AISpeechStream(whole.SampleRate, One(whole.Pcm)) : null;

        var options = found.Options.Speech ?? new AISpeechOptions();
        if (text.Length > options.MaxCharacters)
            throw new AIRequestTooLargeException(found.Name, "characters", options.MaxCharacters);

        var lease = gate.Acquire(found, caller);
        try
        {
            await gate.CheckAsync(found, caller, cancellationToken);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        var request = new AISpeechRequest(found.Name, found.Model, text, options, caller);
        return new AISpeechStream(provider.SampleRate, Pieces());

        // The lease is held and the meter runs until the last piece is read, or the reader stops.
        async IAsyncEnumerable<ReadOnlyMemory<byte>> Pieces([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken stop = default)
        {
            using var held = lease;
            var started = meter.Time.GetTimestamp();
            Exception? error = null;
            var pieces = provider.StreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);

            try
            {
                while (true)
                {
                    try
                    {
                        if (!await pieces.MoveNextAsync()) break;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        error = e;
                        throw;
                    }

                    yield return pieces.Current;
                }
            }
            finally
            {
                await pieces.DisposeAsync();
                await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, error, characters: error is null ? text.Length : 0);
            }
        }

        static async IAsyncEnumerable<ReadOnlyMemory<byte>> One(ReadOnlyMemory<byte> pcm)
        {
            await Task.CompletedTask;
            yield return pcm;
        }
    }

    public AIFeatureState StateOf(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (!registry.Current.Features.TryGetValue(feature, out var found)) return AIFeatureState.NotConfigured;

        return found.Off switch
        {
            AIOffReason.Disabled => AIFeatureState.Disabled,
            not null => AIFeatureState.NotConfigured,
            null when gate.IsSpent(found.Name) => AIFeatureState.BudgetSpent,
            null => AIFeatureState.On,
        };
    }

    public ValueTask<AICaller> IdentifyAsync(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return options.IdentifyAsync(request);
    }

    /// <summary>The functions <paramref name="feature"/> lists that the server knows, with the sides that have them.</summary>
    internal IReadOnlyList<AIRealtimeTool> FunctionsOf(AIFeature feature)
    {
        var tools = new List<AIRealtimeTool>();

        foreach (var name in feature.Options.Functions ?? [])
        {
            var server = options.Functions.ServerFunction(name);
            var device = options.Functions.DeviceFunction(name);
            if ((server ?? device) is not { } declared) continue;

            tools.Add(new AIRealtimeTool(declared.Name, declared.Description, declared.JsonSchema, server is not null, device is not null, declared.Prefer));
        }

        return tools;
    }

    /// <summary>The feature, when it is configured, enabled and not over a known hard budget.</summary>
    internal bool TryGetOn(string feature, out AIFeature found)
    {
        found = null!;
        if (!registry.Current.Features.TryGetValue(feature, out var configured)) return false;

        found = configured;
        return configured.Off is null && !gate.IsSpent(configured.Name);
    }

    /// <summary>Why <paramref name="feature"/> is off, for the keyed clients and the endpoints.</summary>
    internal AIOffReason OffReason(string feature) => StateOf(feature) switch
    {
        AIFeatureState.Disabled => AIOffReason.Disabled,
        AIFeatureState.BudgetSpent => AIOffReason.Budget,
        _ => AIOffReason.NotConfigured,
    };
}
