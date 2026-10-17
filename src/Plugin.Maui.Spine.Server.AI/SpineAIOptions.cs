using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>A budget passed a level.</summary>
/// <param name="Scope">The app (<c>*|*</c>) or a feature (<c>Name|*</c>).</param>
/// <param name="Period">The day or month.</param>
/// <param name="Level">Soft: still running. Hard: off for the rest of the period.</param>
/// <param name="Spent">What the scope has spent in the period.</param>
/// <param name="Limit">The limit that was passed.</param>
/// <param name="Currency">The app's currency.</param>
public sealed record AIBudgetAlert(AIUsageScope Scope, AIPeriod Period, AIBudgetLevel Level, decimal Spent, decimal Limit, string Currency)
{
    /// <summary>A line for a push or a log, such as <c>RaceStory spent 2.0412 USD on 2026-10-12, over the soft limit of 2 USD.</c></summary>
    public override string ToString()
    {
        var who = Scope.Feature == "*" ? "The app" : Scope.Feature;
        var when = Period.Kind == AIPeriodKind.Day ? $"on {Period.Key[2..]}" : $"in {Period.Key[2..]}";
        var level = Level == AIBudgetLevel.Soft ? "soft" : "hard";
        return string.Create(CultureInfo.InvariantCulture,
            $"{who} spent {Spent:0.####} {Currency} {when}, over the {level} limit of {Limit:0.####} {Currency}.");
    }
}

/// <summary>The code half of Spine's AI setup: providers, the usage store and the callbacks.</summary>
/// <remarks>The rest (features, models, prices, budgets) is configuration, under the section given to <c>AddSpineAI</c>.</remarks>
public sealed class SpineAIOptions
{
    internal Dictionary<string, Func<IConfiguration, string, IChatClient>> ChatProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, Func<IConfiguration, string, IImageGenerator>> ImageProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, Func<IConfiguration, IAIRealtimeProvider>> RealtimeProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, Func<IConfiguration, IAISpeechProvider>> SpeechProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Func<IServiceProvider, IAIUsageStore>? StoreFactory { get; private set; }

    /// <summary>
    /// Says who a request is for. The default takes the authenticated user's <c>sub</c> (or name identifier), then
    /// the <c>Spine-Installation</c> header, then the remote address.
    /// </summary>
    public Func<HttpRequest, ValueTask<AICaller>>? Identify { get; set; }

    /// <summary>Decides who may read <c>GET /ai/usage</c>. Unset, nobody may: it answers 401.</summary>
    public Func<HttpRequest, ValueTask<bool>>? AuthorizeReport { get; set; }

    /// <summary>
    /// Called once per scope, period and level when the app's or a feature's budget passes its soft or hard
    /// limit. A push to the owner is one line; see the wiki. An exception here is logged, not passed to the call.
    /// </summary>
    public Func<AIBudgetAlert, IServiceProvider, CancellationToken, Task>? OnBudgetThreshold { get; set; }

    /// <summary>Registers a chat provider: the vendor's own Microsoft.Extensions.AI adapter, built from configuration.</summary>
    /// <param name="provider">The name features use, and the key under <c>Ai:Providers</c>.</param>
    /// <param name="factory">Builds a client from the provider's section and a model id.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
    /// </code>
    /// </example>
    public SpineAIOptions Chat(string provider, Func<IConfiguration, string, IChatClient> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ChatProviders[provider] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Registers an image provider.</summary>
    /// <param name="provider">The name features use, and the key under <c>Ai:Providers</c>.</param>
    /// <param name="factory">Builds a generator from the provider's section and a model id.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIOptions Image(string provider, Func<IConfiguration, string, IImageGenerator> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ImageProviders[provider] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Registers a realtime provider, which sets up voice sessions the app connects to itself.</summary>
    /// <param name="provider">The name features use, and the key under <c>Ai:Providers</c>.</param>
    /// <param name="factory">
    /// Builds the provider from its section: <see cref="AIRealtimeProviders.OpenAI(IConfiguration)"/>,
    /// <see cref="AIRealtimeProviders.AzureOpenAI(IConfiguration)"/> or one of your own.
    /// </param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// ai.Realtime("openai", AIRealtimeProviders.OpenAI);
    /// </code>
    /// </example>
    public SpineAIOptions Realtime(string provider, Func<IConfiguration, IAIRealtimeProvider> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        RealtimeProviders[provider] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Registers a speech provider, which reads text aloud for <c>Kind: Speech</c> features.</summary>
    /// <param name="provider">The name features use, and the key under <c>Ai:Providers</c>.</param>
    /// <param name="factory">Builds the provider from its section: <see cref="AISpeechProviders.OpenAI(IConfiguration)"/> or one of your own.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// ai.Speech("openai", AISpeechProviders.OpenAI);
    /// </code>
    /// </example>
    public SpineAIOptions Speech(string provider, Func<IConfiguration, IAISpeechProvider> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        SpeechProviders[provider] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Keeps usage in the process. For tests and samples: budgets reset on restart.</summary>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIOptions UseInMemoryUsageStore()
    {
        var store = new InMemoryAIUsageStore();
        StoreFactory = _ => store;
        return this;
    }

    /// <summary>Keeps usage in Azure Table Storage.</summary>
    /// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
    /// <param name="prefix">Prepended to both table names, so several apps can share an account.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIOptions UseAzureTableUsageStore(string connectionString, string prefix = "SpineAI")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        StoreFactory = _ => new AzureTableAIUsageStore(connectionString, prefix);
        return this;
    }

    /// <summary>Uses a usage store of your own.</summary>
    /// <param name="factory">Builds the store from the application's services.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIOptions UseUsageStore(Func<IServiceProvider, IAIUsageStore> factory)
    {
        StoreFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    internal void Validate()
    {
        if (StoreFactory is null)
            throw new InvalidOperationException(
                "AddSpineAI: pick a usage store with UseInMemoryUsageStore(), UseAzureTableUsageStore(...) or UseUsageStore(...).");
    }

    internal ValueTask<AICaller> IdentifyAsync(HttpRequest request) =>
        Identify is { } identify ? identify(request) : ValueTask.FromResult(DefaultIdentify(request));

    internal static AICaller DefaultIdentify(HttpRequest request)
    {
        var user = request.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true
            && (user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier)) is { Value.Length: > 0 } subject)
            return AICaller.SignedIn(subject.Value);

        // Bounded, because the id ends up in a table key and the report.
        if (request.Headers[AICaller.InstallationHeader].ToString() is { Length: > 0 and <= 128 } installation)
            return AICaller.Installation(installation);

        return AICaller.Anonymous(request.HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
