using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

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
    /// asks the provider for a short-lived token and reserves the session's minutes.
    /// </summary>
    /// <param name="feature">The feature's name; its <c>Kind</c> is <c>Realtime</c>.</param>
    /// <param name="caller">Who the session is for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the app connects with, or <see langword="null"/> when the feature is off or not a realtime feature.</returns>
    /// <exception cref="AIFeatureException">The caller is refused, over a rate or out of budget.</exception>
    Task<AIRealtimeSession?> RealtimeAsync(string feature, AICaller caller, CancellationToken cancellationToken = default);

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

    /// <summary>Whether <paramref name="feature"/> answers right now.</summary>
    /// <param name="feature">The feature's name.</param>
    AIFeatureState StateOf(string feature);

    /// <summary>Who <paramref name="request"/> is for, by <see cref="SpineAIOptions.Identify"/>.</summary>
    /// <param name="request">The incoming request.</param>
    ValueTask<AICaller> IdentifyAsync(HttpRequest request);
}

internal sealed class AIFeatures(
    AIFeatureRegistry registry, AIBudgetGate gate, AIUsageMeter meter, SpineAIOptions options, IServiceProvider services) : IAIFeatures
{
    public IChatClient? Chat(string feature, AICaller caller)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Chat is not { } shared) return null;

        IChatClient client = new CallerChatClient(shared, found, caller, gate, meter);

        // Outside the gate and the meter: a cached answer costs nothing and is served even when the budget is spent.
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

    public async Task<AIRealtimeSession?> RealtimeAsync(string feature, AICaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(caller);

        if (!TryGetOn(feature, out var found) || found.Realtime is not { } provider) return null;

        using var lease = gate.Acquire(found, caller);
        await gate.CheckAsync(found, caller, cancellationToken);

        var session = found.Options.Realtime ?? new AIRealtimeOptions();
        var request = new AIRealtimeRequest(found.Name, found.Model, found.Options.SystemPrompt, found.Options.MaxOutputTokens, session, caller);
        var started = meter.Time.GetTimestamp();

        try
        {
            var made = await provider.CreateSessionAsync(request, cancellationToken);
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, null, minutes: Math.Max(0, session.MaxMinutes));
            return made;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await meter.RecordAsync(found, caller, found.Model, started, null, 0, 0, e);
            throw;
        }
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
