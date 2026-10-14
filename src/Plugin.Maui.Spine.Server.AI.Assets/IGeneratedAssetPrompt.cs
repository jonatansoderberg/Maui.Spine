using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>
/// The app's half of a generated asset: which keys exist, and the request that makes the image for one. Spine never
/// sees the domain data behind a key.
/// </summary>
/// <typeparam name="TKey">
/// The key. Its <see cref="object.ToString"/> is the asset's name in blob paths and must parse back to an equal key:
/// letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, with <c>/</c> between segments.
/// </typeparam>
/// <remarks>Resolved in a new scope for every lookup that orders and for every generation.</remarks>
public interface IGeneratedAssetPrompt<in TKey> where TKey : IParsable<TKey>
{
    /// <summary>
    /// Whether <paramref name="key"/> names something the app knows. Asked only before an order, so an anonymous lookup
    /// cannot spend the budget on keys that mean nothing.
    /// </summary>
    ValueTask<bool> AcceptsAsync(TKey key, CancellationToken cancellationToken);

    /// <summary>
    /// The request that makes the image, with original images for an edit. Throw
    /// <see cref="Messaging.UnrecoverableMessageException"/> when the key can never be made, such as when its data is gone.
    /// </summary>
    ValueTask<ImageGenerationRequest> BuildAsync(TKey key, CancellationToken cancellationToken);
}

/// <summary>Looks at a generated image before it is archived. A refused image is made again, until the attempts run out.</summary>
/// <typeparam name="TKey">The asset's key.</typeparam>
public interface IGeneratedAssetCheck<in TKey> where TKey : IParsable<TKey>
{
    /// <summary>Whether <paramref name="image"/> may be kept.</summary>
    ValueTask<GeneratedAssetCheckResult> CheckAsync(TKey key, DataContent image, CancellationToken cancellationToken);
}

/// <summary>What an <see cref="IGeneratedAssetCheck{TKey}"/> found.</summary>
/// <param name="Passed">Whether the image may be kept.</param>
/// <param name="Reason">Why not, for the log.</param>
public readonly record struct GeneratedAssetCheckResult(bool Passed, string? Reason = null)
{
    /// <summary>The image may be kept.</summary>
    public static GeneratedAssetCheckResult Pass { get; } = new(true);

    /// <summary>The image is refused.</summary>
    /// <param name="reason">Why, for the log.</param>
    public static GeneratedAssetCheckResult Fail(string reason) => new(false, reason);
}
