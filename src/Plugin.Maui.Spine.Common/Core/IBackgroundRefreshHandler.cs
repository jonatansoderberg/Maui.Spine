namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Runs when the platform grants the app a background run for its widgets — on iOS a
/// <c>BGAppRefreshTask</c>, on Android an alarm — before every widget is rebuilt. Register with
/// <see cref="SpineWidgetsOptions.UseBackgroundRefresh{THandler}"/> to sync data first; the widgets
/// are refreshed afterwards whether or not a handler is registered. Resolved through DI on every run.
/// With <c>Plugin.Maui.Spine.BackgroundTasks</c> in the app, the handler and the rebuild run as that
/// package's built-in task <c>spine.widgets</c>, scheduled with the app's own tasks.
/// </summary>
public interface IBackgroundRefreshHandler
{
    /// <summary>
    /// Does the app's background work. The platform's time is short — about 30 seconds on iOS, 20 seconds
    /// on Android — and <paramref name="cancellationToken"/> is signalled when it runs out. On Android a
    /// handler still running 5 seconds after that is left behind and the run is ended without it, so honour
    /// the token.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
