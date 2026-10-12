using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// One named feature: its sources tried in order. A source is skipped when it is unavailable, and the next is tried
/// when it throws before its first update. Once text has arrived there is no fallback, and a cancelled call never
/// falls back.
/// </summary>
internal sealed class SpineFeatureChatClient : IChatClient
{
    internal const string AnsweredByKey = "spine.answeredBy";

    private readonly string _feature;
    private readonly SpineAIRuntime _runtime;
    private readonly (AISource Source, Lazy<IChatClient> Client)[] _sources;

    public SpineFeatureChatClient(string feature, AIFeatureChain chain, SpineAIRuntime runtime, IServiceProvider services)
    {
        _feature = feature;
        _runtime = runtime;
        _sources = [.. chain.Sources.Select(source => (source, new Lazy<IChatClient>(() => source.Kind == AIAnsweredBy.Server
            ? new SpineServerChatClient(source.ServerFeature ?? feature, runtime)
            : new DeviceMeteredChatClient(source.Factory!(services), feature, runtime))))];
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        Exception? last = null;

        foreach (var (source, client) in _sources)
        {
            if (!IsAvailable(source)) continue;

            try
            {
                var response = await client.Value.GetResponseAsync(list, options, cancellationToken);
                (response.AdditionalProperties ??= [])[AnsweredByKey] = source.Kind;
                return response;
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                last = FellThrough(source, e);
            }
        }

        throw last ?? NothingAvailable();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var (updates, answeredBy) = await StartAsync(list, options, cancellationToken);

        await using (updates)
        {
            var first = updates.Current;
            (first.AdditionalProperties ??= [])[AnsweredByKey] = answeredBy;
            yield return first;

            while (await updates.MoveNextAsync())
                yield return updates.Current;
        }
    }

    /// <summary>The first source that gives an update, positioned on that update.</summary>
    private async Task<(IAsyncEnumerator<ChatResponseUpdate> Updates, AIAnsweredBy AnsweredBy)> StartAsync(
        IList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        Exception? last = null;

        foreach (var (source, client) in _sources)
        {
            if (!IsAvailable(source)) continue;

            IAsyncEnumerator<ChatResponseUpdate>? updates = null;
            try
            {
                updates = client.Value.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
                if (await updates.MoveNextAsync()) return (updates, source.Kind);

                last = new InvalidOperationException($"The {source.Kind} source of '{_feature}' ended its stream without an update.");
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                last = FellThrough(source, e);
            }

            if (updates is not null) await updates.DisposeAsync();
        }

        throw last ?? NothingAvailable();
    }

    private bool IsAvailable(AISource source)
    {
        try
        {
            return source.IsAvailable?.Invoke() ?? true;
        }
        catch (Exception e)
        {
            _runtime.Logger.LogWarning(e, "AI feature '{Feature}': the {Source} source's isAvailable threw, so it is skipped.", _feature, source.Kind);
            return false;
        }
    }

    private Exception FellThrough(AISource source, Exception e)
    {
        _runtime.Logger.LogWarning(e, "AI feature '{Feature}': the {Source} source failed before it answered; trying the next one.", _feature, source.Kind);
        return e;
    }

    private Exception NothingAvailable() =>
        new AIFeatureOffException(_feature, AIOffReason.NotConfigured);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        foreach (var (_, client) in _sources)
        {
            if (client.IsValueCreated) client.Value.Dispose();
        }
    }
}

/// <summary>Where a feature's answer came from.</summary>
public static class AIAnswers
{
    /// <summary>The source that answered, or <see langword="null"/> when the response did not come through a Spine.AI feature.</summary>
    public static AIAnsweredBy? AnsweredBy(this ChatResponse response) => From(response.AdditionalProperties);

    /// <summary>The source that answered. Set on a stream's first update only.</summary>
    public static AIAnsweredBy? AnsweredBy(this ChatResponseUpdate update) => From(update.AdditionalProperties);

    private static AIAnsweredBy? From(AdditionalPropertiesDictionary? properties) =>
        properties?.TryGetValue(SpineFeatureChatClient.AnsweredByKey, out var value) == true && value is AIAnsweredBy by ? by : null;
}
