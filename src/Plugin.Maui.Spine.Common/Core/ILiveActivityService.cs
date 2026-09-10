namespace Plugin.Maui.Spine.Common;

/// <summary>Starts, updates and ends Live Activities from the app.</summary>
public interface ILiveActivityService
{
    /// <summary>Whether the current platform shows Live Activities.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the user allows this app's Live Activities; they can be turned off in Settings.</summary>
    bool AreActivitiesEnabled { get; }

    /// <summary>The activities started by this app that are still alive.</summary>
    IReadOnlyList<LiveActivity> Active { get; }

    /// <summary>
    /// Raised when <see cref="Active"/> changed: an activity started or ended, by the app or by the
    /// platform — the user swiped it away, a push ended it, it aged past its stale date, a push
    /// started one. The platform reports a change while the app runs and at the next foreground
    /// otherwise; a handle from <see cref="Active"/> has <see cref="LiveActivity.IsEnded"/> set by then.
    /// A registration built from these can be sent again: a server addresses an activity by its own
    /// push token, and that token exists only while the activity does — without this, a backend
    /// keeps sending to one that is gone, which APNs accepts and drops, leaving no error to go on.
    /// Raised on whatever thread the platform reported on; dispatch before touching UI.
    /// </summary>
    event Action? ActivitiesChanged;

    /// <summary>
    /// Raised for each activity that ended without the app ending it: the user swiped it away, a push
    /// ended it, it aged past its stale date — or it ended while the app was not running, which is
    /// reported at the next launch. <see cref="ActivitiesChanged"/> is raised as well. The app's own
    /// <see cref="LiveActivity.EndAsync"/> raises nothing here; the caller already knows.
    /// </summary>
    /// <remarks>
    /// To hear about activities that ended while the app was not running, subscribe as early as the
    /// service exists — right after <c>builder.Build()</c> in <c>MauiProgram</c>. They are reported as
    /// the app launches, before any page has appeared.
    /// </remarks>
    event Action<LiveActivity>? ActivityEnded;

    /// <summary>
    /// Starts an activity of <paramref name="kind"/> with <paramref name="layout"/>. Returns
    /// <see langword="null"/> when the platform refused, for example because activities are
    /// disabled or the app is not in the foreground.
    /// </summary>
    /// <param name="kind">Stable identifier used to tell activities apart; not shown to the user.</param>
    /// <param name="layout">The trees per region.</param>
    /// <param name="staleAt">When the content should be presented as out of date if no update arrived.</param>
    Task<LiveActivity?> StartAsync(string kind, LiveActivityLayout layout, DateTimeOffset? staleAt = null);

    /// <summary>Ends every active activity immediately.</summary>
    Task EndAllAsync();

    /// <summary>
    /// The token a server uses to start an activity by push (iOS 17.2+), as hex, or <see langword="null"/>
    /// when the platform has none — push tokens off in <see cref="SpineWidgetsOptions.LiveActivityPushTokens"/>,
    /// no push entitlement, or none issued yet. Waits a few seconds for one to arrive. Tokens rotate, so
    /// send it to the server at every launch and foreground.
    /// </summary>
    Task<string?> GetPushToStartTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>A handle to one running Live Activity.</summary>
public sealed class LiveActivity
{
    private readonly Func<LiveActivity, LiveActivityLayout, DateTimeOffset?, Task> _update;
    private readonly Func<LiveActivity, Task> _end;
    private readonly Func<LiveActivity, CancellationToken, Task<string?>> _pushToken;

    /// <summary>Constructed by the platform's <see cref="ILiveActivityService"/>, not by app code.</summary>
    /// <param name="id">The platform's id for the running activity.</param>
    /// <param name="kind">The kind it was started with.</param>
    /// <param name="update">Pushes a new layout, with an optional stale date.</param>
    /// <param name="end">Ends the activity.</param>
    /// <param name="pushToken">Fetches the activity's push token, or <see langword="null"/> when it has none.</param>
    public LiveActivity(string id, string kind,
        Func<LiveActivity, LiveActivityLayout, DateTimeOffset?, Task> update,
        Func<LiveActivity, Task> end,
        Func<LiveActivity, CancellationToken, Task<string?>> pushToken)
    {
        Id = id;
        Kind = kind;
        _update = update;
        _end = end;
        _pushToken = pushToken;
    }

    /// <summary>The platform's identifier of the activity.</summary>
    public string Id { get; }

    /// <summary>The kind it was started with.</summary>
    public string Kind { get; }

    /// <summary>
    /// Whether the activity is over — <see cref="EndAsync"/> was called, or the platform reported it
    /// gone: dismissed by the user, ended by a push, or past its stale date.
    /// </summary>
    public bool IsEnded { get; set; }

    /// <summary>Replaces the content with <paramref name="layout"/>.</summary>
    public Task UpdateAsync(LiveActivityLayout layout, DateTimeOffset? staleAt = null) => _update(this, layout, staleAt);

    /// <summary>Ends the activity and removes it from the Lock Screen and Dynamic Island.</summary>
    public Task EndAsync() => _end(this);

    /// <summary>
    /// The token a server updates this activity with by push, as hex, or <see langword="null"/> when the
    /// platform issued none (see <see cref="ILiveActivityService.GetPushToStartTokenAsync"/>). Waits a few
    /// seconds for it to arrive after a start.
    /// </summary>
    public Task<string?> GetPushTokenAsync(CancellationToken cancellationToken = default) => _pushToken(this, cancellationToken);
}
