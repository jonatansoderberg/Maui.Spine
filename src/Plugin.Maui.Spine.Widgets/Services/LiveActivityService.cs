using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Widgets.Services;

internal sealed class LiveActivityService(IWidgetPlatform _platform, WidgetIconAssets _icons) : ILiveActivityService
{
    private readonly List<LiveActivity> _active = [];
    private bool _adopted;

    public bool IsSupported => _platform.IsSupported;

    public bool AreActivitiesEnabled => _platform.AreActivitiesEnabled;

    public IReadOnlyList<LiveActivity> Active
    {
        get { lock (_active) { Adopt(); return [.. _active]; } }
    }

    /// <inheritdoc />
    public event Action? ActivitiesChanged;

    /// <summary>
    /// Picks up the activities the platform is still showing from before this process started. An
    /// activity outlives the app that started it, so without this the app would offer to start a
    /// second one on top of the first after a relaunch.
    /// </summary>
    private void Adopt()
    {
        if (_adopted || !_platform.IsSupported) return;
        _adopted = true;

        foreach (var (id, kind) in _platform.ActiveActivities())
            if (!_active.Any(a => a.Id == id))
                _active.Add(new LiveActivity(id, kind, Update, End, PushToken));
    }

    public async Task<LiveActivity?> StartAsync(string kind, LiveActivityLayout layout, DateTimeOffset? staleAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!_platform.IsSupported) return null;

        await _icons.EnsureAsync(layout, CancellationToken.None);
        var id = await _platform.StartActivityAsync(kind, WidgetJson.Serialize(layout), staleAt);
        if (id is null) return null;

        var activity = new LiveActivity(id, kind, Update, End, PushToken);
        lock (_active) { Adopt(); _active.Add(activity); }

        // The token does not exist yet — ActivityKit issues it a moment later. Raising now is still
        // right: whoever rebuilds a registration reads the token through GetPushTokenAsync, which
        // waits for it. Raising once the token arrived would need a second mechanism for no gain.
        ActivitiesChanged?.Invoke();

        return activity;
    }

    public async Task EndAllAsync()
    {
        foreach (var activity in Active)
            await activity.EndAsync();
    }

    public Task<string?> GetPushToStartTokenAsync(CancellationToken cancellationToken = default) =>
        PollAsync(() => _platform.PushToStartToken, cancellationToken);

    private Task<string?> PushToken(LiveActivity activity, CancellationToken cancellationToken) =>
        PollAsync(() => _platform.PushToken(activity.Id), cancellationToken);

    // ActivityKit hands tokens out a moment after the request, on its own schedule; a few seconds
    // covers it, and none by then means the platform is not going to issue one.
    private async Task<string?> PollAsync(Func<string?> read, CancellationToken cancellationToken)
    {
        if (!_platform.IsSupported) return null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (read() is { Length: > 0 } token) return token;
            await Task.Delay(500, cancellationToken);
        }
        return read();
    }

    private async Task Update(LiveActivity activity, LiveActivityLayout layout, DateTimeOffset? staleAt)
    {
        if (activity.IsEnded) throw new InvalidOperationException("The activity has ended.");
        await _icons.EnsureAsync(layout, CancellationToken.None);
        _platform.UpdateActivity(activity.Id, WidgetJson.Serialize(layout), staleAt);
    }

    private Task End(LiveActivity activity)
    {
        if (activity.IsEnded) return Task.CompletedTask;
        activity.IsEnded = true;
        lock (_active) _active.Remove(activity);
        _platform.EndActivity(activity.Id);
        ActivitiesChanged?.Invoke();
        return Task.CompletedTask;
    }
}
