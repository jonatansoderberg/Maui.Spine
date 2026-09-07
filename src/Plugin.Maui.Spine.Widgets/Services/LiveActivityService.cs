using Plugin.Maui.Spine.Widgets.Serialization;

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
                _active.Add(new LiveActivity(id, kind, Update, End));
    }

    public async Task<LiveActivity?> StartAsync(string kind, LiveActivityLayout layout, DateTimeOffset? staleAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!_platform.IsSupported) return null;

        await _icons.EnsureAsync(layout, CancellationToken.None);
        var id = await _platform.StartActivityAsync(kind, WidgetJson.Serialize(layout), staleAt);
        if (id is null) return null;

        var activity = new LiveActivity(id, kind, Update, End);
        lock (_active) { Adopt(); _active.Add(activity); }
        return activity;
    }

    public async Task EndAllAsync()
    {
        foreach (var activity in Active)
            await activity.EndAsync();
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
        return Task.CompletedTask;
    }
}
