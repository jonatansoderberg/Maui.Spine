namespace Plugin.Maui.Spine.Common;

/// <summary>Refreshes widgets from the app.</summary>
public interface IWidgetService
{
    /// <summary>Whether the current platform renders Spine widgets.</summary>
    bool IsSupported { get; }

    /// <summary>The widget kinds discovered from the Spine assemblies.</summary>
    IReadOnlyList<string> Kinds { get; }

    /// <summary>Rebuilds the timeline of <paramref name="kind"/> and asks the platform to reload it.</summary>
    Task RefreshAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds the timeline of the widget provided by <typeparamref name="TProvider"/>.</summary>
    Task RefreshAsync<TProvider>(CancellationToken cancellationToken = default) where TProvider : IWidgetProvider;

    /// <summary>Rebuilds every widget's timeline. Also runs automatically when the app moves to the background.</summary>
    Task RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a bitmap in the shared container so trees can show it with <see cref="W.Image"/>.
    /// Keep images small: the renderer runs under a tight memory limit.
    /// </summary>
    Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken = default);

    /// <summary>The URL <see cref="WidgetTimeline.OpenUrl"/> should use to open the app at <paramref name="kind"/>.</summary>
    Uri LinkFor(string kind);

    /// <summary>
    /// iOS 26's widget push token as hex, or <see langword="null"/> — before iOS 26, without
    /// <c>SpineWidgetsPush</c>, or before WidgetKit has issued one. Spine.Push sends it to the backend,
    /// which can then reload the widgets by push without waking the app.
    /// </summary>
    string? PushToken { get; }

    /// <summary>Raised when <see cref="PushToken"/> may have changed, so a registration can carry the new one.</summary>
    event Action? PushTokenChanged;
}
