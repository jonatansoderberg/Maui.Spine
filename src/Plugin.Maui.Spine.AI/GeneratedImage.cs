using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.AI;

/// <summary>Where an <see cref="Image"/> with <see cref="GeneratedImage.FeatureProperty"/> stands.</summary>
public enum GeneratedImageStatus
{
    /// <summary>No feature or key is set.</summary>
    None,

    /// <summary>The server is making the image; the placeholder shows.</summary>
    Pending,

    /// <summary>The image shows.</summary>
    Ready,

    /// <summary>The image failed, the key is unknown, the feature is off, or the backend could not be reached; <see cref="GeneratedImage.FailedSourceProperty"/> shows.</summary>
    Failed,
}

/// <summary>
/// Shows a generated image from the app's backend on any <see cref="Image"/>: a placeholder while the server makes it,
/// the image when it is ready, and a failed source when it cannot be made. The server is asked again while the image is
/// pending (see <see cref="IAIAssets.WatchAsync"/>), and not while the view is off screen.
/// </summary>
/// <example>
/// <code>
/// &lt;Image ai:GeneratedImage.Feature="Postcard"
///        ai:GeneratedImage.Key="{Binding City}"
///        ai:GeneratedImage.Placeholder="postcard_blank.png"
///        ai:GeneratedImage.FailedSource="postcard_missing.png" /&gt;
/// </code>
/// </example>
public static class GeneratedImage
{
    /// <summary>The asset feature's name on the server.</summary>
    public static readonly BindableProperty FeatureProperty = BindableProperty.CreateAttached(
        "Feature", typeof(string), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Restart(view));

    /// <summary>The image's key, as the server's key type writes it.</summary>
    public static readonly BindableProperty KeyProperty = BindableProperty.CreateAttached(
        "Key", typeof(string), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Restart(view));

    /// <summary>Shown while the image is pending, and when it fails unless <see cref="FailedSourceProperty"/> is set.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.CreateAttached(
        "Placeholder", typeof(ImageSource), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Reapply(view));

    /// <summary>Shown when the image failed, its key is unknown, the feature is off or the backend could not be reached.</summary>
    public static readonly BindableProperty FailedSourceProperty = BindableProperty.CreateAttached(
        "FailedSource", typeof(ImageSource), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Reapply(view));

    /// <summary>
    /// A view shown only while the image is pending, such as an <c>ActivityIndicator</c> over the image, given with
    /// <c>{x:Reference}</c>. MAUI bindings cannot follow an attached property, so the image shows and hides it itself.
    /// </summary>
    public static readonly BindableProperty PendingViewProperty = BindableProperty.CreateAttached(
        "PendingView", typeof(VisualElement), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Reapply(view));

    /// <summary>A view shown only when the image failed, such as a label with the reason, given with <c>{x:Reference}</c>.</summary>
    public static readonly BindableProperty FailedViewProperty = BindableProperty.CreateAttached(
        "FailedView", typeof(VisualElement), typeof(GeneratedImage), null, propertyChanged: (view, _, _) => Reapply(view));

    private static readonly BindablePropertyKey StatusPropertyKey = BindableProperty.CreateAttachedReadOnly(
        "Status", typeof(GeneratedImageStatus), typeof(GeneratedImage), GeneratedImageStatus.None);

    /// <summary>
    /// Where the image stands, for the image's own triggers and for code. For other views, use
    /// <see cref="PendingViewProperty"/> and <see cref="FailedViewProperty"/>: MAUI bindings cannot follow an attached property.
    /// </summary>
    public static readonly BindableProperty StatusProperty = StatusPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AssetPropertyKey = BindableProperty.CreateAttachedReadOnly(
        "Asset", typeof(AIAsset), typeof(GeneratedImage), null);

    /// <summary>The server's last answer, with the failure reason; <see langword="null"/> when the backend could not be reached.</summary>
    public static readonly BindableProperty AssetProperty = AssetPropertyKey.BindableProperty;

    private static readonly BindableProperty WatchProperty = BindableProperty.CreateAttached(
        "Watch", typeof(CancellationTokenSource), typeof(GeneratedImage), null);

    private static readonly BindableProperty FollowedProperty = BindableProperty.CreateAttached(
        "Followed", typeof(bool), typeof(GeneratedImage), false);

    /// <summary>Gets the feature.</summary>
    public static string? GetFeature(BindableObject view) => (string?)view.GetValue(FeatureProperty);

    /// <summary>Sets the feature.</summary>
    public static void SetFeature(BindableObject view, string? value) => view.SetValue(FeatureProperty, value);

    /// <summary>Gets the key.</summary>
    public static string? GetKey(BindableObject view) => (string?)view.GetValue(KeyProperty);

    /// <summary>Sets the key.</summary>
    public static void SetKey(BindableObject view, string? value) => view.SetValue(KeyProperty, value);

    /// <summary>Gets the placeholder.</summary>
    public static ImageSource? GetPlaceholder(BindableObject view) => (ImageSource?)view.GetValue(PlaceholderProperty);

    /// <summary>Sets the placeholder.</summary>
    public static void SetPlaceholder(BindableObject view, ImageSource? value) => view.SetValue(PlaceholderProperty, value);

    /// <summary>Gets the failed source.</summary>
    public static ImageSource? GetFailedSource(BindableObject view) => (ImageSource?)view.GetValue(FailedSourceProperty);

    /// <summary>Sets the failed source.</summary>
    public static void SetFailedSource(BindableObject view, ImageSource? value) => view.SetValue(FailedSourceProperty, value);

    /// <summary>Gets the view shown while pending.</summary>
    public static VisualElement? GetPendingView(BindableObject view) => (VisualElement?)view.GetValue(PendingViewProperty);

    /// <summary>Sets the view shown while pending.</summary>
    public static void SetPendingView(BindableObject view, VisualElement? value) => view.SetValue(PendingViewProperty, value);

    /// <summary>Gets the view shown on failure.</summary>
    public static VisualElement? GetFailedView(BindableObject view) => (VisualElement?)view.GetValue(FailedViewProperty);

    /// <summary>Sets the view shown on failure.</summary>
    public static void SetFailedView(BindableObject view, VisualElement? value) => view.SetValue(FailedViewProperty, value);

    /// <summary>Gets where the image stands.</summary>
    public static GeneratedImageStatus GetStatus(BindableObject view) => (GeneratedImageStatus)view.GetValue(StatusProperty);

    /// <summary>Gets the server's last answer.</summary>
    public static AIAsset? GetAsset(BindableObject view) => (AIAsset?)view.GetValue(AssetProperty);

    private static void Restart(BindableObject view)
    {
        if (view is not Image image)
            return;

        Follow(image);
        Stop(image);

        if (GetFeature(image) is not { Length: > 0 } feature || GetKey(image) is not { Length: > 0 } key)
        {
            Show(image, GeneratedImageStatus.None, null, null);
            return;
        }

        // Started from Loaded: a view in a list that is built but never shown asks nothing. A recycled cell rebound
        // while detached must not keep the last key's image, so it starts over from nothing.
        if (!image.IsLoaded)
        {
            Show(image, GeneratedImageStatus.None, null, null);
            return;
        }

        var services = image.Handler?.MauiContext?.Services ?? Application.Current?.Handler?.MauiContext?.Services;
        if (services?.GetService<IAIAssets>() is not { } assets)
        {
            Show(image, GeneratedImageStatus.Failed, null, null);
            services?.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.AI")
                .LogError("GeneratedImage: no IAIAssets is registered, so '{Feature}/{Key}' cannot be looked up. Call UseSpineAI with the backend.", feature, key);
            return;
        }

        var watch = new CancellationTokenSource();
        image.SetValue(WatchProperty, watch);
        Show(image, GeneratedImageStatus.Pending, null, null);
        _ = RunAsync(image, assets, feature, key, services, watch.Token);
    }

    private static async Task RunAsync(Image image, IAIAssets assets, string feature, string key, IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            AIAsset? last = null;
            await foreach (var asset in assets.WatchAsync(feature, key, cancellationToken: cancellationToken))
            {
                last = asset;
                image.Dispatcher.Dispatch(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var status = asset.Status switch
                    {
                        AIAssetStatus.Ready => GeneratedImageStatus.Ready,
                        AIAssetStatus.Pending or AIAssetStatus.RateLimited => GeneratedImageStatus.Pending,
                        _ => GeneratedImageStatus.Failed,
                    };
                    Show(image, status, asset, asset.Url);
                });
            }

            // The watch gave up while the image was still being made: failed rather than a spinner that never ends.
            // The next time the image is loaded, it asks again.
            if (last?.Status is AIAssetStatus.Pending or AIAssetStatus.RateLimited)
            {
                image.Dispatcher.Dispatch(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                        Show(image, GeneratedImageStatus.Failed, last, null);
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.AI")
                .LogWarning(e, "GeneratedImage: looking up '{Feature}/{Key}' failed.", feature, key);
            image.Dispatcher.Dispatch(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    Show(image, GeneratedImageStatus.Failed, null, null);
            });
        }
    }

    private static void Show(Image image, GeneratedImageStatus status, AIAsset? asset, Uri? url)
    {
        image.SetValue(StatusPropertyKey, status);
        image.SetValue(AssetPropertyKey, asset);
        if (GetPendingView(image) is { } pending)
            pending.IsVisible = status == GeneratedImageStatus.Pending;
        if (GetFailedView(image) is { } failed)
            failed.IsVisible = status == GeneratedImageStatus.Failed;
        image.Source = status switch
        {
            GeneratedImageStatus.Ready when url is not null => ImageSource.FromUri(url),
            GeneratedImageStatus.Failed => GetFailedSource(image) ?? GetPlaceholder(image),
            GeneratedImageStatus.None => null,
            _ => GetPlaceholder(image),
        };
    }

    private static void Reapply(BindableObject view)
    {
        if (view is not Image image)
            return;

        if (GetStatus(image) is GeneratedImageStatus.Pending or GeneratedImageStatus.Failed)
            Show(image, GetStatus(image), GetAsset(image), null);
        else
        {
            if (GetPendingView(image) is { } pending) pending.IsVisible = false;
            if (GetFailedView(image) is { } failed) failed.IsVisible = false;
        }
    }

    private static void Stop(Image image)
    {
        if (image.GetValue(WatchProperty) is CancellationTokenSource watch)
        {
            watch.Cancel();
            watch.Dispose();
            image.ClearValue(WatchProperty);
        }
    }

    private static void Follow(Image image)
    {
        if ((bool)image.GetValue(FollowedProperty))
            return;

        image.SetValue(FollowedProperty, true);
        image.Loaded += (_, _) =>
        {
            if (GetStatus(image) != GeneratedImageStatus.Ready)
                Restart(image);
        };
        image.Unloaded += (_, _) =>
        {
            // An image off screen asks nothing; it asks again when it is back, unless it is already showing.
            if (GetStatus(image) != GeneratedImageStatus.Ready)
                Stop(image);
        };
    }
}
