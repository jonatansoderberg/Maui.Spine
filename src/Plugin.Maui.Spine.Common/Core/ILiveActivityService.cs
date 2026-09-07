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

    /// <summary>Whether <see cref="EndAsync"/> has been called.</summary>
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
