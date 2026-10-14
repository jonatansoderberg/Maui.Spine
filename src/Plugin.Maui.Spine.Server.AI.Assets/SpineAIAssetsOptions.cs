using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>The code half of generated assets: which features make them, with which prompt, and where they are kept.</summary>
public sealed partial class SpineAIAssetsOptions
{
    internal Dictionary<string, AssetRegistration> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Func<IServiceProvider, IGeneratedAssetStore>? StoreFactory { get; private set; }

    /// <summary>How long a ready answer is kept in memory before the store is asked again. Five minutes by default.</summary>
    public TimeSpan ReadyCacheTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a pending answer tells the app to wait before it asks again. 15 seconds by default.</summary>
    public TimeSpan PendingRetryAfter { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long an order holds: within it, lookups of the same image order nothing more. It must outlast the
    /// generation and its retries. One hour by default.
    /// </summary>
    public TimeSpan OrderWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Makes images for the image feature <paramref name="feature"/>, with <typeparamref name="TPrompt"/> building the requests.</summary>
    /// <param name="feature">The feature's name under <c>Ai:Features</c>; its <c>Kind</c> must be <c>Image</c>.</param>
    /// <typeparam name="TKey">The key type, such as a record that parses from its <c>ToString()</c>, or <see cref="string"/>.</typeparam>
    /// <typeparam name="TPrompt">The app's prompt, resolved in a new scope.</typeparam>
    /// <returns>The asset, to add a check to.</returns>
    public GeneratedAssetBuilder<TKey> Asset<TKey, TPrompt>(string feature)
        where TKey : IParsable<TKey>
        where TPrompt : class, IGeneratedAssetPrompt<TKey>
    {
        if (!FeatureName().IsMatch(feature ?? ""))
            throw new ArgumentException($"'{feature}' is not a valid asset feature name: use 1–60 letters, digits, '_' or '-'.", nameof(feature));
        if (Assets.ContainsKey(feature!))
            throw new InvalidOperationException($"The asset feature '{feature}' is registered twice.");

        var registration = new AssetRegistration<TKey>(feature!, typeof(TPrompt));
        Assets[feature!] = registration;
        return new GeneratedAssetBuilder<TKey>(registration);
    }

    /// <summary>Archives images in an Azure blob container that the app reads directly.</summary>
    /// <param name="configure">The connection, the container and a CDN.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIAssetsOptions UseAzureBlobStore(Action<AzureBlobAssetStoreOptions>? configure = null)
    {
        var options = new AzureBlobAssetStoreOptions();
        configure?.Invoke(options);
        StoreFactory = sp => AzureBlobGeneratedAssetStore.Create(
            sp.GetRequiredService<IConfiguration>(), options,
            sp.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Server.AI.Assets") ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return this;
    }

    /// <summary>Keeps images in the process. For tests and samples.</summary>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIAssetsOptions UseInMemoryStore(Uri? baseUrl = null)
    {
        StoreFactory = sp => new InMemoryGeneratedAssetStore(baseUrl, sp.GetService<TimeProvider>());
        return this;
    }

    /// <summary>Uses a store of your own.</summary>
    /// <returns>The same options, for chaining.</returns>
    public SpineAIAssetsOptions UseStore(Func<IServiceProvider, IGeneratedAssetStore> factory)
    {
        StoreFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    internal void Validate()
    {
        if (StoreFactory is null)
            throw new InvalidOperationException("AddSpineAIAssets: pick a store with UseAzureBlobStore(...), UseInMemoryStore() or UseStore(...).");
        if (Assets.Count == 0)
            throw new InvalidOperationException("AddSpineAIAssets: no feature makes assets. Call Asset<TKey, TPrompt>(\"Feature\").");
        if (ReadyCacheTime < TimeSpan.Zero || PendingRetryAfter <= TimeSpan.Zero || OrderWindow <= TimeSpan.Zero)
            throw new InvalidOperationException("AddSpineAIAssets: ReadyCacheTime may not be negative, and PendingRetryAfter and OrderWindow must be positive.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,60}$")]
    private static partial Regex FeatureName();
}

/// <summary>One feature's asset, as declared by <see cref="SpineAIAssetsOptions.Asset{TKey, TPrompt}"/>.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
public sealed class GeneratedAssetBuilder<TKey> where TKey : IParsable<TKey>
{
    private readonly AssetRegistration<TKey> _registration;

    internal GeneratedAssetBuilder(AssetRegistration<TKey> registration) => _registration = registration;

    /// <summary>Checks every image with <typeparamref name="TCheck"/> before it is archived.</summary>
    /// <typeparam name="TCheck">The check, resolved in the generation's scope.</typeparam>
    /// <returns>The same builder.</returns>
    public GeneratedAssetBuilder<TKey> Check<TCheck>() where TCheck : class, IGeneratedAssetCheck<TKey>
    {
        _registration.CheckType = typeof(TCheck);
        return this;
    }
}

/// <summary>A feature's asset with its key type erased, so the lookup and the handler work on the key's text.</summary>
internal abstract partial class AssetRegistration(string feature, Type promptType)
{
    private const int MaxKeyLength = 200;

    public string Feature { get; } = feature;
    public Type PromptType { get; } = promptType;
    public Type? CheckType { get; set; }
    public abstract Type KeyType { get; }

    /// <summary>The canonical text of <paramref name="text"/>'s key, or <see langword="null"/> when it does not parse or is not a safe path.</summary>
    public abstract string? Canonical(string text);

    public abstract ValueTask<bool> AcceptsAsync(IServiceProvider services, string canonical, CancellationToken cancellationToken);

    public abstract ValueTask<ImageGenerationRequest> BuildAsync(IServiceProvider services, string canonical, CancellationToken cancellationToken);

    /// <summary>The check's verdict, or a pass when the asset has no check.</summary>
    public abstract ValueTask<GeneratedAssetCheckResult> CheckAsync(IServiceProvider services, string canonical, DataContent image, CancellationToken cancellationToken);

    public abstract void Register(IServiceCollection services);

    protected static bool IsSafe(string canonical) => canonical.Length <= MaxKeyLength && SafeKey().IsMatch(canonical);

    // Segments start with a letter or digit, so a key can never climb out of its version with "..".
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*$")]
    private static partial Regex SafeKey();
}

internal sealed class AssetRegistration<TKey>(string feature, Type promptType) : AssetRegistration(feature, promptType)
    where TKey : IParsable<TKey>
{
    public override Type KeyType => typeof(TKey);

    public override string? Canonical(string text)
    {
        if (!TKey.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var key) || key.ToString() is not { } canonical)
            return null;

        // The handler parses the canonical text again, so it has to come back as the same key.
        return IsSafe(canonical) && TKey.TryParse(canonical, System.Globalization.CultureInfo.InvariantCulture, out var again)
            && EqualityComparer<TKey>.Default.Equals(key, again)
            ? canonical
            : null;
    }

    public override ValueTask<bool> AcceptsAsync(IServiceProvider services, string canonical, CancellationToken cancellationToken) =>
        Prompt(services).AcceptsAsync(Parse(canonical), cancellationToken);

    public override ValueTask<ImageGenerationRequest> BuildAsync(IServiceProvider services, string canonical, CancellationToken cancellationToken) =>
        Prompt(services).BuildAsync(Parse(canonical), cancellationToken);

    public override ValueTask<GeneratedAssetCheckResult> CheckAsync(IServiceProvider services, string canonical, DataContent image, CancellationToken cancellationToken) =>
        CheckType is null
            ? ValueTask.FromResult(GeneratedAssetCheckResult.Pass)
            : ((IGeneratedAssetCheck<TKey>)services.GetRequiredService(CheckType)).CheckAsync(Parse(canonical), image, cancellationToken);

    public override void Register(IServiceCollection services)
    {
        services.AddScoped(PromptType);
        if (CheckType is not null)
            services.AddScoped(CheckType);
    }

    private IGeneratedAssetPrompt<TKey> Prompt(IServiceProvider services) => (IGeneratedAssetPrompt<TKey>)services.GetRequiredService(PromptType);

    private static TKey Parse(string canonical) => TKey.Parse(canonical, System.Globalization.CultureInfo.InvariantCulture);
}
