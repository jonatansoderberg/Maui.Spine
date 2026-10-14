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
