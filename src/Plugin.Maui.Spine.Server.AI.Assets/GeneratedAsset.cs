namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>Where a generated asset stands.</summary>
public enum GeneratedAssetStatus
{
    /// <summary>The image is archived; <see cref="GeneratedAsset.Url"/> points at it.</summary>
    Ready,

    /// <summary>It is ordered and being made. Ask again after <see cref="GeneratedAsset.RetryAfter"/>.</summary>
    Pending,

    /// <summary>The last attempt failed. It is ordered again on the first lookup after <see cref="GeneratedAsset.RetryAfter"/>.</summary>
    Failed,

    /// <summary>The key does not parse, or the app's prompt does not know it.</summary>
    NotFound,

    /// <summary>The feature is off and no image is archived; <see cref="GeneratedAsset.Off"/> says why.</summary>
    Off,

    /// <summary>The caller ordered too many images; ask again after <see cref="GeneratedAsset.RetryAfter"/>.</summary>
    RateLimited,
}

/// <summary>Why a generated asset failed. The provider's own message goes to the log only.</summary>
public enum GeneratedAssetFailure
{
    /// <summary>The model call failed, or answered without an image.</summary>
    Model,

    /// <summary>The app's <see cref="IGeneratedAssetCheck{TKey}"/> refused every image.</summary>
    Check,

    /// <summary>The app's <see cref="IGeneratedAssetPrompt{TKey}"/> could not build the request.</summary>
    Prompt,

    /// <summary>The app's or the feature's budget is spent.</summary>
    Budget,

    /// <summary>The feature is disabled or not configured.</summary>
    Off,

    /// <summary>The image was made but could not be archived.</summary>
    Storage,
}

/// <summary>The answer to a lookup.</summary>
/// <param name="Status">Where the asset stands.</param>
/// <param name="Url">The image, with <c>?v=</c> from its last change so a remade image gets a new URL. Set when ready.</param>
/// <param name="Failure">Why it failed. Set when failed.</param>
/// <param name="RetryAfter">When to ask again. Set when pending, failed or rate limited.</param>
/// <param name="Off">Why the feature is off. Set when off.</param>
public sealed record GeneratedAsset(
    GeneratedAssetStatus Status,
    Uri? Url = null,
    GeneratedAssetFailure? Failure = null,
    DateTimeOffset? RetryAfter = null,
    AIOffReason? Off = null);
