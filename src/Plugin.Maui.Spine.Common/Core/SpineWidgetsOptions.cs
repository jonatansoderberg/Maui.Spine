namespace Plugin.Maui.Spine.Common;

/// <summary>Options for <c>UseSpineWidgets</c> in <c>Plugin.Maui.Spine.Widgets</c>.</summary>
public sealed class SpineWidgetsOptions
{
    /// <summary>
    /// The App Group the app and the widget extension share on Apple platforms. Leave
    /// <see langword="null"/> to use the value the build wrote into Info.plist from the
    /// <c>SpineWidgetsAppGroup</c> property (default <c>group.&lt;ApplicationId&gt;</c>).
    /// </summary>
    public string? AppGroup { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default) every widget is rebuilt as the app moves to the
    /// background, so the home screen shows the state the user just left.
    /// </summary>
    public bool RefreshOnBackground { get; set; } = true;

    /// <summary>
    /// How often the app asks the platform for a background run that rebuilds the widgets (and runs
    /// the <see cref="IBackgroundRefreshHandler"/>, if any). A request, not a promise: iOS and Android
    /// both stretch it when the device is idle. <see cref="TimeSpan.Zero"/> turns the runs off.
    /// </summary>
    public TimeSpan BackgroundRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// When <see langword="true"/>, Live Activities are started with a push token on iOS and the
    /// tokens are available through <see cref="ILiveActivityService.GetPushToStartTokenAsync"/> and
    /// <see cref="LiveActivity.GetPushTokenAsync"/>. Needs the push notification entitlement.
    /// </summary>
    public bool LiveActivityPushTokens { get; set; }

    public Type? BackgroundRefreshHandler { get; private set; }

    /// <summary>Runs <typeparamref name="THandler"/> before the widgets are rebuilt in a background run.</summary>
    public SpineWidgetsOptions UseBackgroundRefresh<THandler>() where THandler : class, IBackgroundRefreshHandler
    {
        BackgroundRefreshHandler = typeof(THandler);
        return this;
    }
}
