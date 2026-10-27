using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.AI;

/// <summary>A function the server's feature lists: its declaration, the sides that can run it, and the side it prefers.</summary>
internal sealed record SpineCatalogueFunction(string Name, string Description, JsonElement Parameters, bool OnServer, bool OnDevice, SpineAIFunctionSide Prefer);

/// <summary>
/// A server feature's catalogue (<c>GET {Backend}/features/{name}/functions</c>): the one list of what its sessions may
/// call, and its limits. Fetched once per feature and kept for ten minutes.
/// </summary>
internal sealed record SpineFeatureCatalogue(string Feature, IReadOnlyList<SpineCatalogueFunction> Functions, int MaxToolRounds, int MaxResultCharacters)
{
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<(SpineAIRuntime, string), (SpineFeatureCatalogue? Catalogue, DateTimeOffset At)> Cache = new();

    /// <summary>The catalogue of <paramref name="feature"/>, or <see langword="null"/> when the server has none for the app (an older server, or a feature it does not expose).</summary>
    public static async Task<SpineFeatureCatalogue?> GetAsync(SpineAIRuntime runtime, string feature, CancellationToken cancellationToken)
    {
        var now = runtime.Time.GetUtcNow();
        if (Cache.TryGetValue((runtime, feature), out var kept) && now - kept.At < Keep)
            return kept.Catalogue;

        SpineFeatureCatalogue? catalogue = null;
        if (runtime.Options.Backend is { } backend)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{backend.ToString().TrimEnd('/')}/features/{Uri.EscapeDataString(feature)}/functions"));
                await runtime.AddHeadersAsync(request, cancellationToken);
                using var response = await runtime.Http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    catalogue = Parse(feature, await response.Content.ReadAsStringAsync(cancellationToken));
                else
                    runtime.Logger.LogWarning("AI feature '{Feature}': no catalogue from the server ({Status}); the model on the device gets the app's functions only.", feature, (int)response.StatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                runtime.Logger.LogWarning(e, "AI feature '{Feature}': the catalogue could not be read; the model on the device gets the app's functions only.", feature);
                return null;
            }
        }

        Cache[(runtime, feature)] = (catalogue, now);
        return catalogue;
    }

    internal static SpineFeatureCatalogue? Parse(string feature, string json)
    {
        var root = JsonNode.Parse(json);
        if (root?["tools"] is not JsonArray tools)
            return null;

        var functions = new List<SpineCatalogueFunction>();
        foreach (var tool in tools.OfType<JsonObject>())
        {
            if ((string?)tool["name"] is not { Length: > 0 } name)
                continue;
            var runsOn = tool["runsOn"] is JsonArray sides ? sides.Select(s => (string?)s).ToHashSet() : [];
            using var schema = JsonDocument.Parse(tool["parameters"]?.ToJsonString() ?? """{"type":"object","properties":{}}""");
            functions.Add(new SpineCatalogueFunction(name, (string?)tool["description"] ?? "", schema.RootElement.Clone(),
                runsOn.Contains("server"), runsOn.Contains("device"),
                (string?)tool["prefer"] == "server" ? SpineAIFunctionSide.Server : SpineAIFunctionSide.Device));
        }

        return new SpineFeatureCatalogue(feature, functions,
            (int?)root["maxToolRounds"] is > 0 and var rounds ? rounds : 4,
            (int?)root["maxResultCharacters"] is > 0 and var cap ? cap : SpineAIFunction.DefaultMaxResultCharacters);
    }
}

/// <summary>
/// A server function offered to a model on the device: the model calls it like any tool, and the call goes to
/// <c>POST {Backend}/functions/{feature}/{name}</c>, where it runs with the caller's sign-in and context.
/// </summary>
internal sealed class ForwardedFunction(SpineCatalogueFunction declared, string feature, SpineAIRuntime runtime) : AIFunction
{
    public override string Name => declared.Name;
    public override string Description => declared.Description;
    public override JsonElement JsonSchema => declared.Parameters;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["callId"] = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId,
            ["arguments"] = JsonSerializer.SerializeToNode(arguments.ToDictionary(p => p.Key, p => p.Value), AIJsonUtilities.DefaultOptions),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"{runtime.Options.Backend!.ToString().TrimEnd('/')}/functions/{Uri.EscapeDataString(feature)}/{Uri.EscapeDataString(Name)}"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        await runtime.AddHeadersAsync(request, cancellationToken);

        using var response = await runtime.Http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = text.Length > 0 ? JsonNode.Parse(text) : null;

        if (response.IsSuccessStatusCode && json?["result"] is { } result)
            return JsonSerializer.SerializeToElement(result, AIJsonUtilities.DefaultOptions);

        // The server's refusals reach the model as from every other path, so it can tell the user why.
        if (AIOffReasons.FromJson((string?)json?["off"]) is { } off)
            return off == AIOffReason.SignInRequired
                ? SpineAIFunctionResult.SignInRequired()
                : SpineAIFunctionResult.Error($"The function '{Name}' is not available now ({off}).", "refused");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return SpineAIFunctionResult.Error("Too many function calls; try again later.", "refused");

        runtime.Logger.LogWarning("AI feature '{Feature}': the forwarded call to '{Function}' got {Status}.", feature, Name, (int)response.StatusCode);
        return SpineAIFunctionResult.Error($"The function '{Name}' failed.");
    }
}
