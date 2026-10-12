using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// A feature's chat pipeline for one caller: limits, then budget, then the meter around the shared pipeline.
/// Made per call, so the caller never enters the options and two callers asking the same thing share a cache entry.
/// </summary>
internal sealed class CallerChatClient(IChatClient inner, AIFeature feature, AICaller caller, AIBudgetGate gate, AIUsageMeter meter)
    : DelegatingChatClient(inner)
{
    // The pipeline underneath is the feature's, shared by every caller; disposing one call's client must not end it.
    protected override void Dispose(bool disposing) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();

        using var lease = gate.Acquire(feature, caller);
        await gate.CheckAsync(feature, caller, cancellationToken);

        var started = meter.Time.GetTimestamp();
        ChatResponse? response = null;
        Exception? error = null;

        try
        {
            response = await base.GetResponseAsync(list, options, cancellationToken);
            return response;
        }
        catch (Exception e)
        {
            error = e;
            throw;
        }
        finally
        {
            await meter.RecordAsync(feature, caller, options?.ModelId ?? feature.Model, started, response?.Usage, 0, AIUsageMeter.Estimate(list), error);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();

        using var lease = gate.Acquire(feature, caller);
        await gate.CheckAsync(feature, caller, cancellationToken);

        var started = meter.Time.GetTimestamp();
        UsageDetails? usage = null;
        Exception? error = null;

        var updates = base.GetStreamingResponseAsync(list, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        // The finally runs when the stream ends, fails, or the caller stops reading (the enumerator is disposed),
        // so a stream cut short is recorded with what it used up to then.
        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                try
                {
                    if (!await updates.MoveNextAsync()) break;
                    update = updates.Current;
                }
                catch (Exception e)
                {
                    error = e;
                    throw;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent { Details: { } details }) (usage ??= new()).Add(details);
                }

                yield return update;
            }
        }
        finally
        {
            await updates.DisposeAsync();
            await meter.RecordAsync(feature, caller, options?.ModelId ?? feature.Model, started, usage, 0, AIUsageMeter.Estimate(list), error);
        }
    }
}

/// <summary>The image counterpart of <see cref="CallerChatClient"/>.</summary>
internal sealed class CallerImageGenerator(IImageGenerator inner, AIFeature feature, AICaller caller, AIBudgetGate gate, AIUsageMeter meter)
    : DelegatingImageGenerator(inner)
{
    protected override void Dispose(bool disposing) { }

    public override async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var lease = gate.Acquire(feature, caller);
        await gate.CheckAsync(feature, caller, cancellationToken);

        var started = meter.Time.GetTimestamp();
        ImageGenerationResponse? response = null;
        Exception? error = null;

        try
        {
            response = await base.GenerateAsync(request, options, cancellationToken);
            return response;
        }
        catch (Exception e)
        {
            error = e;
            throw;
        }
        finally
        {
            var images = response?.Contents.Count(c => c is DataContent or UriContent) ?? 0;
            var estimated = ((request.Prompt?.Length ?? 0) + 3) / 4;
            await meter.RecordAsync(feature, caller, options?.ModelId ?? feature.Model, started, response?.Usage, images, estimated, error);
        }
    }
}

/// <summary>
/// Microsoft.Extensions.AI's response cache with the feature's <c>Cache:Ttl</c>; its own keeps entries until the
/// cache evicts them. Written in the format the base class reads.
/// </summary>
internal sealed class ExpiringCachingChatClient(IChatClient inner, IDistributedCache cache, TimeSpan ttl)
    : DistributedCachingChatClient(inner, cache)
{
    private readonly DistributedCacheEntryOptions _entry = new() { AbsoluteExpirationRelativeToNow = ttl };

    protected override void Dispose(bool disposing) { }

    protected override Task WriteCacheAsync(string key, ChatResponse value, CancellationToken cancellationToken) =>
        cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.GetTypeInfo(typeof(ChatResponse))), _entry, cancellationToken);

    protected override Task WriteCacheStreamingAsync(string key, IReadOnlyList<ChatResponseUpdate> value, CancellationToken cancellationToken) =>
        cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.GetTypeInfo(typeof(IReadOnlyList<ChatResponseUpdate>))), _entry, cancellationToken);
}

/// <summary>
/// What <c>[FromKeyedServices("Name")] IChatClient</c> resolves to: the feature for <see cref="AICaller.System"/>,
/// looked up at every call so a configuration change or a spent budget is seen. A singleton, so it can go into
/// singletons. While the feature is off, every call throws <see cref="AIFeatureOffException"/>.
/// </summary>
internal sealed class FeatureChatClient(string feature, AIFeatures features) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Current().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Current().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : features.Chat(feature, AICaller.System)?.GetService(serviceType, serviceKey);

    public void Dispose() { }

    private IChatClient Current() =>
        features.Chat(feature, AICaller.System) ?? throw new AIFeatureOffException(feature, features.OffReason(feature));
}

/// <summary>The image counterpart of <see cref="FeatureChatClient"/>.</summary>
internal sealed class FeatureImageGenerator(string feature, AIFeatures features) : IImageGenerator
{
    public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        (features.Image(feature, AICaller.System) ?? throw new AIFeatureOffException(feature, features.OffReason(feature)))
            .GenerateAsync(request, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : features.Image(feature, AICaller.System)?.GetService(serviceType, serviceKey);

    public void Dispose() { }
}
