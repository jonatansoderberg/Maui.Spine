namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Builds the content of one widget kind. Implementations are decorated with
/// <see cref="WidgetAttribute"/>, discovered from the Spine assemblies, and constructed through
/// DI on every refresh, so constructor injection works as in a view model.
/// </summary>
public interface IWidgetProvider
{
    /// <summary>
    /// Builds the timeline the platform should show from now on. Called when the app refreshes
    /// the widget explicitly, and when the app moves to the background.
    /// </summary>
    Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken);
}

/// <summary>What a provider is asked to build.</summary>
/// <param name="Kind">The widget kind being built.</param>
public sealed record WidgetContext(string Kind);

/// <summary>
/// Implemented by a provider that wants to handle the user opening the app from its widget.
/// Resolved through DI like the provider itself.
/// </summary>
public interface IWidgetLinkHandler
{
    /// <summary>Called on the main thread when the app was opened from the widget.</summary>
    Task OnWidgetOpenedAsync(WidgetLink link);
}

/// <summary>The link the app was opened with.</summary>
/// <param name="Kind">The widget kind the link belongs to.</param>
/// <param name="Url">The full URL, so custom query values set with <see cref="WidgetTimeline.OpenUrl"/> can be read.</param>
public sealed record WidgetLink(string Kind, Uri Url);

/// <summary>
/// Implemented by a provider whose trees contain <see cref="W.Button"/>. Resolved through DI like the
/// provider itself; the widget is rebuilt after the handler returns, so what the tap changed shows.
/// Runs in the app's process at once, whether the app is in the foreground, in the background or not
/// running — the platform launches it in the background for the tap.
/// </summary>
public interface IWidgetActionHandler
{
    /// <summary>Called on the main thread when a button in the widget was tapped.</summary>
    Task OnActionAsync(WidgetAction action);
}

/// <summary>A tapped button.</summary>
/// <param name="Kind">The widget kind the button belongs to.</param>
/// <param name="ActionId">The id given to <see cref="W.Button"/>.</param>
/// <param name="At">
/// When the button was tapped. Normally the same instant as the handler, since the tap runs in the
/// app's process on both platforms; on iOS it is the tap's time even when the widget extension had
/// to record the tap for the app's next launch, which a handler that stamps
/// <see cref="DateTimeOffset.Now"/> would get wrong.
/// </param>
public sealed record WidgetAction(string Kind, string ActionId, DateTimeOffset At);
