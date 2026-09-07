namespace Plugin.Maui.Spine.Widgets;

/// <summary>
/// Runs when the platform grants the app a background run for its widgets — on iOS a
/// <c>BGAppRefreshTask</c>, on Android an alarm — before every widget is rebuilt. Register with
/// <see cref="SpineWidgetsOptions.UseBackgroundRefresh{THandler}"/> to sync data first; the widgets
/// are refreshed afterwards whether or not a handler is registered. Resolved through DI on every run.
/// </summary>
public interface IBackgroundRefreshHandler
{
    /// <summary>
    /// Does the app's background work. The platform's time is short (about 30 seconds on iOS);
    /// <paramref name="cancellationToken"/> is signalled when it runs out.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
