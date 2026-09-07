namespace Plugin.Maui.Spine.Widgets;

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
/// </summary>
public interface IWidgetActionHandler
{
    /// <summary>Called on the main thread when a button in the widget was tapped.</summary>
    Task OnActionAsync(WidgetAction action);
}

/// <summary>A tapped button.</summary>
/// <param name="Kind">The widget kind the button belongs to.</param>
/// <param name="ActionId">The id given to <see cref="W.Button"/>.</param>
public sealed record WidgetAction(string Kind, string ActionId);
