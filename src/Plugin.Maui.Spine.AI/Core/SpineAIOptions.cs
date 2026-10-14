using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.AI;

/// <summary>Where a feature's answer came from.</summary>
public enum AIAnsweredBy
{
    /// <summary>A model on the device, such as Apple Intelligence.</summary>
    Device,

    /// <summary>The app's Spine.Server.AI backend.</summary>
    Server,
}

/// <summary>How Spine.AI reaches the app's models: the backend and the named features.</summary>
public sealed class SpineAIOptions
{
    internal Dictionary<string, AIFeatureChain> Features { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The backend's AI prefix, the one given to <c>MapSpineAI</c> on the server, such as
    /// <c>https://api.example.com/ai</c>. Features call <c>POST {Backend}/features/{name}</c>.
    /// </summary>
    public Uri? Backend { get; set; }

    /// <summary>
    /// The value sent as the <c>Authorization</c> header. Read on every call, so a token that rotates can be
    /// returned from here. Unset, the server knows the app by its installation id only.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? AuthorizationHeader { get; set; }

    /// <summary>
    /// The handler under the client's <see cref="HttpClient"/>. Unset, the platform's default. For tests, or to
    /// add certificate pinning or logging.
    /// </summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>
    /// Declares a feature and where it runs. The feature is then injected as
    /// <c>[FromKeyedServices("Name")] IChatClient</c>.
    /// </summary>
    /// <param name="name">The feature's name, as the server knows it.</param>
    /// <param name="configure">The sources, tried in order.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// o.Feature("Assistant", f => f.OnDevice(_ => new AppleIntelligenceChatClient()).ThenServer());
    /// o.Feature("RaceStory", f => f.Server());
    /// </code>
    /// </example>
    public SpineAIOptions Feature(string name, Action<AIFeatureChain> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var chain = new AIFeatureChain();
        configure(chain);
        if (chain.Sources.Count == 0)
            throw new ArgumentException($"The AI feature '{name}' needs a source: OnDevice(...) or Server().", nameof(configure));

        Features[name] = chain;
        return this;
    }
}

/// <summary>Where a feature runs, in the order the sources are tried.</summary>
/// <remarks>
/// A source is skipped when it is unavailable, and the next is tried when it throws before its first update. Once text
/// has arrived there is no fallback: the user has seen it. A cancelled call never falls back.
/// </remarks>
public sealed class AIFeatureChain
{
    internal List<AISource> Sources { get; } = [];

    /// <summary>A model on the device, such as <c>AppleIntelligenceChatClient</c> from Microsoft.Maui.Essentials.AI.</summary>
    /// <param name="factory">Builds the client, once, the first time the feature is called.</param>
    /// <param name="isAvailable">
    /// Asked before every call; <see langword="false"/> skips the device. A client that is not ready usually throws at
    /// once, which falls back too, so this is for what the app knows up front (the OS version, a setting).
    /// </param>
    /// <returns>The same chain.</returns>
    public AIFeatureChain OnDevice(Func<IServiceProvider, IChatClient> factory, Func<bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Sources.Add(new AISource(AIAnsweredBy.Device, factory, isAvailable, null));
        return this;
    }

    /// <summary>The app's Spine.Server.AI backend, at <see cref="SpineAIOptions.Backend"/>.</summary>
    /// <param name="feature">The feature's name on the server, when it differs from the name the app uses.</param>
    /// <returns>The same chain.</returns>
    public AIFeatureChain Server(string? feature = null)
    {
        Sources.Add(new AISource(AIAnsweredBy.Server, null, null, feature));
        return this;
    }

    /// <summary>The same as <see cref="Server"/>, for reading after <see cref="OnDevice"/>.</summary>
    /// <param name="feature">The feature's name on the server, when it differs from the name the app uses.</param>
    /// <returns>The same chain.</returns>
    public AIFeatureChain ThenServer(string? feature = null) => Server(feature);
}

/// <summary>One place a feature can run.</summary>
internal sealed record AISource(AIAnsweredBy Kind, Func<IServiceProvider, IChatClient>? Factory, Func<bool>? IsAvailable, string? ServerFeature);
