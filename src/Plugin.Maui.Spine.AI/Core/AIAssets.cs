using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Plugin.Maui.Spine.AI;

/// <summary>Where a generated image stands on the server.</summary>
public enum AIAssetStatus
{
    /// <summary>It is made; <see cref="AIAsset.Url"/> points at it.</summary>
    Ready,

    /// <summary>It is ordered and being made.</summary>
    Pending,

    /// <summary>The server's last attempt failed; <see cref="AIAsset.Failure"/> says why. The server orders it again after <see cref="AIAsset.RetryAfter"/>.</summary>
    Failed,

    /// <summary>The server does not know the key.</summary>
    NotFound,

    /// <summary>The feature is off and no image is made; <see cref="AIAsset.Off"/> says why.</summary>
    Off,

    /// <summary>This installation ordered too many images; ask again after <see cref="AIAsset.RetryAfter"/>.</summary>
    RateLimited,
}

/// <summary>The server's answer for one generated image.</summary>
/// <param name="Status">Where it stands.</param>
/// <param name="Url">The image, when ready. It changes when the image is made again, so an image cache never shows a stale one.</param>
/// <param name="Failure">The server's reason when failed: <c>model</c>, <c>check</c>, <c>prompt</c>, <c>budget</c>, <c>off</c> or <c>storage</c>.</param>
/// <param name="RetryAfter">When to ask again, when pending, failed or rate limited.</param>
/// <param name="Off">Why the feature is off.</param>
public sealed record AIAsset(AIAssetStatus Status, Uri? Url = null, string? Failure = null, DateTimeOffset? RetryAfter = null, AIOffReason? Off = null);

/// <summary>How <see cref="IAIAssets.WatchAsync"/> asks again while an image is being made.</summary>
public sealed class AIAssetWatch
{
    internal static readonly AIAssetWatch Default = new();

    /// <summary>The shortest wait between two questions, whatever the server says. Two seconds by default.</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest wait between two questions. One minute by default.</summary>
    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How long to keep asking. After it the watch ends at its last answer, still pending. Ten minutes by default.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Generated images from the app's backend (<c>Plugin.Maui.Spine.Server.AI.Assets</c>): made once on the server, looked
/// up here by feature and key.
/// </summary>
/// <example>
/// <code>
/// await foreach (var asset in assets.WatchAsync("Postcard", "visby", cancellationToken: ct))
///     Picture = asset.Status == AIAssetStatus.Ready ? ImageSource.FromUri(asset.Url!) : Placeholder;
/// </code>
/// </example>
public interface IAIAssets
{
    /// <summary>Asks once. A missing image is ordered by the server and answers <see cref="AIAssetStatus.Pending"/>.</summary>
    /// <param name="feature">The asset feature's name on the server.</param>
    /// <param name="key">The image's key, as the server's key type writes it; <c>/</c> separates segments.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="HttpRequestException">The backend could not be reached, or answered something that is not an asset answer.</exception>
    Task<AIAsset> LookUpAsync(string feature, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks, and asks again while the image is pending or rate limited, after the server's <c>Retry-After</c>. Yields the
    /// first answer and each change of status. Ends at ready, failed, not found or off, or after
    /// <see cref="AIAssetWatch.MaxWait"/>. In the app, it waits while the app is in the background.
    /// </summary>
    /// <param name="feature">The asset feature's name on the server.</param>
    /// <param name="key">The image's key.</param>
    /// <param name="watch">The intervals; unset, the defaults.</param>
    /// <param name="cancellationToken">Ends the watch, such as when the view goes away.</param>
    IAsyncEnumerable<AIAsset> WatchAsync(string feature, string key, AIAssetWatch? watch = null, CancellationToken cancellationToken = default);
}

internal sealed class AIAssets(SpineAIRuntime runtime) : IAIAssets
{
    public async Task<AIAsset> LookUpAsync(string feature, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var backend = runtime.Options.AssetsBackend ?? runtime.Options.Backend
            ?? throw new InvalidOperationException(
                $"Generated images come from the server, but neither SpineAIOptions.AssetsBackend nor Backend is set. Set one in UseSpineAI to the prefix given to MapSpineAIAssets.");

        // Each segment is escaped, and the '/' between them is kept: the server's route takes the rest of the path as the key.
        var path = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        var url = new Uri($"{backend.ToString().TrimEnd('/')}/assets/{Uri.EscapeDataString(feature)}/{path}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            await runtime.AddHeadersAsync(request, cancellationToken);
            using var response = await runtime.Http.SendAsync(request, cancellationToken);

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = SpineServerChatClient.TryParse(text);
            var status = SpineServerChatClient.Property(json, "status");
            var retryAfter = SpineServerChatClient.RetryAfter(response) is { } wait ? runtime.Time.GetUtcNow() + wait : (DateTimeOffset?)null;

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK when status == "ready" && Uri.TryCreate(SpineServerChatClient.Property(json, "url"), UriKind.Absolute, out var image):
                    return new AIAsset(AIAssetStatus.Ready, image);

                case HttpStatusCode.Accepted when status == "pending":
                    return new AIAsset(AIAssetStatus.Pending, RetryAfter: retryAfter);

                case HttpStatusCode.BadGateway when status == "failed":
                    return new AIAsset(AIAssetStatus.Failed, Failure: SpineServerChatClient.Property(json, "reason"), RetryAfter: retryAfter);

                case HttpStatusCode.NotFound when status == "notFound":
                    return new AIAsset(AIAssetStatus.NotFound);

                case HttpStatusCode.NotFound or HttpStatusCode.Forbidden when AIOffReasons.FromJson(SpineServerChatClient.Property(json, "off")) is { } reason:
                    return new AIAsset(AIAssetStatus.Off, Off: reason);

                case HttpStatusCode.TooManyRequests:
                    return new AIAsset(AIAssetStatus.RateLimited, RetryAfter: retryAfter);
            }

            // A 404 without a status is a route that does not exist: a wrong Backend, or a server without MapSpineAIAssets.
            throw new HttpRequestException(
                $"The generated image '{feature}/{key}' got {(int)response.StatusCode} {response.ReasonPhrase} from {url}: {SpineServerChatClient.Shorten(text)}",
                null, response.StatusCode);
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested && e is not OperationCanceledException)
        {
            throw new OperationCanceledException(null, e, cancellationToken);
        }
    }

    public async IAsyncEnumerable<AIAsset> WatchAsync(
        string feature, string key, AIAssetWatch? watch = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        watch ??= AIAssetWatch.Default;
        var time = runtime.Time;
        var until = time.GetUtcNow() + watch.MaxWait;
        AIAsset? last = null;

        while (true)
        {
            var asset = await LookUpAsync(feature, key, cancellationToken);
            if (last is null || asset.Status != last.Status || asset.Url != last.Url)
                yield return asset;
            last = asset;

            if (asset.Status is not (AIAssetStatus.Pending or AIAssetStatus.RateLimited))
                yield break;

            var now = time.GetUtcNow();
            var wait = asset.RetryAfter is { } at ? at - now : watch.MinInterval;
            wait = TimeSpan.FromTicks(Math.Clamp(wait.Ticks, watch.MinInterval.Ticks, Math.Max(watch.MinInterval.Ticks, watch.MaxInterval.Ticks)));
            if (now + wait > until)
                yield break;

            await Task.Delay(wait, time, cancellationToken);
            await runtime.WhenActiveAsync(cancellationToken);
        }
    }
}
