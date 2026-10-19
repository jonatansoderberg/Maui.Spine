using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>A device function the app says it has, in the body of <c>POST /ai/features/{name}</c>.</summary>
/// <param name="Name">The function's name. Only names the feature lists and the server declared with <c>AddDevice</c> are offered to the model.</param>
/// <param name="Prefer"><c>device</c> or <c>server</c>: where the app wants a function both sides have to run in this chat.</param>
public sealed record AIFunctionOffer(string Name, string? Prefer = null);

/// <summary>The body of <c>POST /ai/functions/{feature}/{name}</c>: a realtime session's call to a server function.</summary>
/// <param name="Arguments">The model's arguments, as the provider sent them.</param>
/// <param name="CallId">The provider's id for the call, for the log.</param>
public sealed record AIFunctionCallBody(JsonElement? Arguments, string? CallId = null);

/// <summary>
/// A feature's functions for one caller: server functions run here with the caller's context, device functions the app
/// offered are declared and handed back to the app, and every round of calls goes through the gate and the meter beneath.
/// </summary>
internal sealed class FeatureFunctionsChatClient(
    IChatClient metered, AIFeature feature, AICaller caller, SpineAIServerFunctions functions, AIPendingCalls pending,
    IServiceProvider services, ILoggerFactory loggerFactory) : DelegatingChatClient(metered)
{
    /// <summary>The <see cref="ChatOptions.AdditionalProperties"/> key the exposed route passes the app's offers under.</summary>
    internal const string OffersKey = "spine.functions.offers";

    internal const int DefaultMaxToolRounds = 4;

    protected override void Dispose(bool disposing) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (invoking, prepared, device) = Prepare(options, cancellationToken);
        var response = await invoking.GetResponseAsync(messages, prepared, cancellationToken);

        foreach (var message in response.Messages)
            message.Contents = await MarkAsync(message.Contents, device, cancellationToken);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (invoking, prepared, device) = Prepare(options, cancellationToken);

        await foreach (var update in invoking.GetStreamingResponseAsync(messages, prepared, cancellationToken))
        {
            if (!update.Contents.Any(c => c is FunctionCallContent))
            {
                yield return update;
                continue;
            }

            // A copy: the function-invoking client still holds the update's calls and runs them after the stream.
            var copy = update.Clone();
            copy.Contents = await MarkAsync(update.Contents, device, cancellationToken);
            yield return copy;
        }
    }

    private (IChatClient Invoking, ChatOptions Options, HashSet<string> Device) Prepare(ChatOptions? options, CancellationToken cancellationToken)
    {
        var prepared = options?.Clone() ?? new ChatOptions();
        var offers = prepared.AdditionalProperties?.TryGetValue(OffersKey, out var value) == true ? value as IReadOnlyList<AIFunctionOffer> : null;
        prepared.AdditionalProperties?.Remove(OffersKey);

        var http = services.GetService<IHttpContextAccessor>()?.HttpContext;
        var context = new SpineAIFunctionContext(caller, SpineAIFunctionSide.Server)
        {
            Feature = feature.Name,
            ConversationId = prepared.ConversationId,
            User = caller.Kind == AICallerKind.SignedIn && http?.User is { } user ? user : new ClaimsPrincipal(new ClaimsIdentity()),
            Services = http?.RequestServices ?? services,
            CancellationToken = cancellationToken,
            MaxResultCharacters = feature.Options.MaxToolResultCharacters ?? SpineAIFunction.DefaultMaxResultCharacters,
        };

        var tools = prepared.Tools is { } own ? new List<AITool>(own) : [];
        var device = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in feature.Options.Functions ?? [])
        {
            var server = functions.ServerFunction(name);
            var offer = offers?.FirstOrDefault(o => o.Name == name);
            var declared = offer is not null ? functions.DeviceFunction(name) : null;

            if (server is not null && (declared is null || Side(offer, server) == SpineAIFunctionSide.Server))
            {
                tools.Add(server.Bind(context));
            }
            else if (declared is not null)
            {
                tools.Add(declared.Declaration());
                device.Add(name);
            }
        }

        prepared.Tools = tools;

        var invoking = new FunctionInvokingChatClient(InnerClient, loggerFactory, context.Services)
        {
            MaximumIterationsPerRequest = Math.Max(1, feature.Options.MaxToolRounds ?? DefaultMaxToolRounds),
        };

        return (invoking, prepared, device);
    }

    private static SpineAIFunctionSide Side(AIFunctionOffer? offer, SpineAIFunction server) => offer?.Prefer?.ToLowerInvariant() switch
    {
        "device" => SpineAIFunctionSide.Device,
        "server" => SpineAIFunctionSide.Server,
        _ => server.Prefer,
    };

    /// <summary>
    /// Calls the server ran are marked informational, so the app shows them but never runs them again; calls to device
    /// functions are kept as pending, the only calls whose results the server will take from the app.
    /// </summary>
    private async Task<IList<AIContent>> MarkAsync(IList<AIContent> contents, HashSet<string> device, CancellationToken cancellationToken)
    {
        var marked = new List<AIContent>(contents.Count);

        foreach (var content in contents)
        {
            if (content is not FunctionCallContent call || call.InformationalOnly)
            {
                marked.Add(content);
                continue;
            }

            if (device.Contains(call.Name))
            {
                await pending.RecordAsync(feature.Name, caller, call, cancellationToken);
                marked.Add(content);
            }
            else
            {
                marked.Add(new FunctionCallContent(call.CallId, call.Name, call.Arguments)
                {
                    InformationalOnly = true,
                    Exception = call.Exception,
                    AdditionalProperties = call.AdditionalProperties,
                });
            }
        }

        return marked;
    }
}

/// <summary>
/// The calls to device functions the server handed to an app and is waiting on. A result is taken only once, only from the
/// caller it was handed to, and only before it expires. In memory, or in the host's <see cref="IDistributedCache"/> when
/// there is one, so another instance can take the result.
/// </summary>
internal sealed class AIPendingCalls(IServiceProvider services, TimeProvider time)
{
    internal static readonly TimeSpan Life = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (string Json, DateTimeOffset Until)> _memory = new(StringComparer.Ordinal);

    public async Task RecordAsync(string feature, AICaller caller, FunctionCallContent call, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new Pending(call.Name, Arguments(call)), SpineAIEndpoints.Json.GetTypeInfo(typeof(Pending)));
        var key = Key(feature, caller, call.CallId);

        if (services.GetService<IDistributedCache>() is { } cache)
        {
            await cache.SetStringAsync(key, json, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Life }, cancellationToken);
            return;
        }

        var now = time.GetUtcNow();
        foreach (var (stale, entry) in _memory)
            if (entry.Until <= now) _memory.TryRemove(stale, out _);

        _memory[key] = (json, now + Life);
    }

    /// <summary>The call the server made with <paramref name="callId"/> for this caller, removed, or <see langword="null"/>.</summary>
    public async Task<FunctionCallContent?> TakeAsync(string feature, AICaller caller, string callId, CancellationToken cancellationToken)
    {
        var key = Key(feature, caller, callId);
        string? json;

        if (services.GetService<IDistributedCache>() is { } cache)
        {
            json = await cache.GetStringAsync(key, cancellationToken);
            if (json is not null) await cache.RemoveAsync(key, cancellationToken);
        }
        else
        {
            json = _memory.TryRemove(key, out var entry) && entry.Until > time.GetUtcNow() ? entry.Json : null;
        }

        if (json is null || JsonSerializer.Deserialize(json, SpineAIEndpoints.Json.GetTypeInfo(typeof(Pending))) is not Pending made) return null;

        return new FunctionCallContent(callId, made.Name,
            made.Arguments?.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal));
    }

    private static string Key(string feature, AICaller caller, string callId) => $"spine-ai-call:{feature}:{caller.Key}:{callId}";

    private static JsonElement? Arguments(FunctionCallContent call) =>
        call.Arguments is null ? null : JsonSerializer.SerializeToElement(call.Arguments, SpineAIEndpoints.Json.GetTypeInfo(typeof(IDictionary<string, object?>)));

    private sealed record Pending(string Name, JsonElement? Arguments);
}
