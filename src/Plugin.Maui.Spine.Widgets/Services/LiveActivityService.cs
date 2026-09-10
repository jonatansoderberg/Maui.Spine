using System.Text.Json;
using System.Text.Json.Serialization;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Widgets.Services;

internal sealed class LiveActivityService(IWidgetPlatform _platform, WidgetIconAssets _icons) : ILiveActivityService
{
    /// <summary>The activities the app last knew about, as id → kind, kept across processes.</summary>
    private const string MemoryKey = "spine.widgets.activities";

    /// <summary>
    /// The channels of those that follow one, as id → channel. Beside <see cref="MemoryKey"/> rather
    /// than in it, so a memory written before channels existed still reads. Android needs it after a
    /// restart, to know which FCM topics the running activities follow.
    /// </summary>
    private const string ChannelMemoryKey = "spine.widgets.activity-channels";

    private readonly List<LiveActivity> _active = [];

    /// <summary>Ended while no one could be told — found by <see cref="Adopt"/> — and told at the next reconcile.</summary>
    private readonly List<LiveActivity> _unannounced = [];

    private bool _adopted;
    private bool _seeded;

    public bool IsSupported => _platform.IsSupported;

    public bool AreActivitiesEnabled => _platform.AreActivitiesEnabled;

    public IReadOnlyList<LiveActivity> Active
    {
        get { lock (_active) { Adopt(); return [.. _active]; } }
    }

    /// <inheritdoc />
    public event Action? ActivitiesChanged;

    /// <inheritdoc />
    public event Action<LiveActivity>? ActivityEnded;

    /// <summary>
    /// Picks up the activities the platform is still showing from before this process started. An
    /// activity outlives the app that started it, so without this the app would offer to start a
    /// second one on top of the first after a relaunch.
    /// </summary>
    /// <remarks>
    /// Runs inside the <see cref="Active"/> getter, where raising an event would be a surprise, so an
    /// activity found to have ended while the app was away is kept for the next <see cref="Reconcile"/>
    /// — at launch, foreground, or the bridge's notice — and announced there.
    /// </remarks>
    private void Adopt()
    {
        if (_adopted || !_platform.IsSupported) return;
        _adopted = true;
        _unannounced.AddRange(Sync().Gone);
    }

    /// <summary>
    /// Brings the list in line with what the platform is actually showing. The platform is the
    /// only one who knows about an activity the user swiped away, one a push ended, one that aged
    /// past its stale date — or one a push started. Raises <see cref="ActivitiesChanged"/> when
    /// anything differed.
    /// </summary>
    internal void Reconcile()
    {
        if (!_platform.IsSupported) return;

        List<LiveActivity> ended;
        bool added;
        lock (_active)
        {
            _adopted = true;
            (var gone, added) = Sync();
            ended = [.. _unannounced, .. gone];
            _unannounced.Clear();
        }

        foreach (var activity in ended) ActivityEnded?.Invoke(activity);
        if (ended.Count > 0 || added) ActivitiesChanged?.Invoke();
    }

    /// <summary>
    /// Brings <c>_active</c> in line with the platform and says what differed. The first time in a
    /// process the list starts from what the app last knew rather than from nothing — which is what
    /// turns an activity that ended while the app was not running into one that is missing now, and
    /// lets the ordinary comparison below find it.
    /// </summary>
    private (List<LiveActivity> Gone, bool Added) Sync()
    {
        if (!_seeded)
        {
            _seeded = true;
            var channels = Remembered(ChannelMemoryKey);
            foreach (var (id, kind) in Remembered(MemoryKey))
            {
                if (!_active.Any(a => a.Id == id)) _active.Add(New(id, kind, channels.GetValueOrDefault(id)));
            }
        }

        var shown = _platform.ActiveActivities();
        var gone = _active.Where(a => !shown.ContainsKey(a.Id)).ToList();
        foreach (var activity in gone)
        {
            activity.IsEnded = true;
            _active.Remove(activity);
        }

        var added = false;
        foreach (var (id, kind) in shown)
            if (!_active.Any(a => a.Id == id))
            {
                _active.Add(New(id, kind, channel: null));
                added = true;
            }

        Remember();
        return (gone, added);
    }

    public async Task<LiveActivity?> StartAsync(string kind, LiveActivityLayout layout, DateTimeOffset? staleAt = null, string? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!_platform.IsSupported) return null;

        await _icons.EnsureAsync(layout, CancellationToken.None);
        lock (_active) Adopt();
        var id = await _platform.StartActivityAsync(kind, WidgetJson.Serialize(layout), staleAt, channel);
        if (id is null) return null;

        // A reconcile can slip in between the start and this add and list the new id already; the
        // caller's handle is the one to keep.
        var activity = New(id, kind, channel);
        lock (_active) { _active.RemoveAll(a => a.Id == id); _active.Add(activity); Remember(); }

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
        lock (_active) { _active.Remove(activity); Remember(); }
        _platform.EndActivity(activity.Id);
        ActivitiesChanged?.Invoke();
        return Task.CompletedTask;
    }

    private LiveActivity New(string id, string kind, string? channel) => new(id, kind, Update, End, PushToken, channel);

    /// <summary>Writes down the current list. Called with the lock held.</summary>
    private void Remember()
    {
        Preferences.Default.Set(MemoryKey, JsonSerializer.Serialize(
            _active.ToDictionary(a => a.Id, a => a.Kind), ActivityMemoryJsonContext.Default.DictionaryStringString));
        Preferences.Default.Set(ChannelMemoryKey, JsonSerializer.Serialize(
            _active.Where(a => a.Channel is not null).ToDictionary(a => a.Id, a => a.Channel!), ActivityMemoryJsonContext.Default.DictionaryStringString));
    }

    private static Dictionary<string, string> Remembered(string key)
    {
        var json = Preferences.Default.Get(key, "");
        if (json is not { Length: > 0 }) return [];

        try
        {
            return JsonSerializer.Deserialize(json, ActivityMemoryJsonContext.Default.DictionaryStringString) ?? [];
        }
        catch (JsonException)
        {
            // A memory that no longer parses is worth less than a working start: forget it, and the
            // next change writes a whole one.
            Preferences.Default.Remove(key);
            return [];
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ActivityMemoryJsonContext : JsonSerializerContext;
