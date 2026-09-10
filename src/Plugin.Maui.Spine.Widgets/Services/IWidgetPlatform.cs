using System.Collections.ObjectModel;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>The platform half of the widget pipeline: storage shared with the renderer and the native calls.</summary>
internal interface IWidgetPlatform
{
    bool IsSupported { get; }

    /// <summary>Writes the timeline document for <paramref name="kind"/> where the renderer reads it.</summary>
    void WriteTimeline(string kind, string json);

    /// <summary>Stores a bitmap under <paramref name="assetId"/> where the renderer reads it.</summary>
    Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken);

    void Reload(string kind);
    void ReloadAll();

    bool AreActivitiesEnabled { get; }

    /// <summary>The activities this app still has running, as id to kind, including any it started before it was last killed.</summary>
    IReadOnlyDictionary<string, string> ActiveActivities();

    /// <summary>Starts an activity and returns its platform id, or <see langword="null"/> when refused. May prompt the user for a permission.</summary>
    Task<string?> StartActivityAsync(string kind, string json, DateTimeOffset? staleAt);
    void UpdateActivity(string id, string json, DateTimeOffset? staleAt);
    void EndActivity(string id);

    /// <summary>The push-to-start token as hex, when the platform has issued one; <see langword="null"/> otherwise.</summary>
    string? PushToStartToken { get; }

    /// <summary>The push token of activity <paramref name="id"/> as hex, when issued.</summary>
    string? PushToken(string id);

    /// <summary>iOS 26's widget push token as hex, when the extension was built with push and WidgetKit issued one.</summary>
    string? WidgetPushToken { get; }

    /// <summary>Asks for the widget push token again; the platform says so through its notification when it changed.</summary>
    void RefreshWidgetPushToken();
}

/// <summary>Used on platforms without a renderer; every call is a no-op so app code stays unconditional.</summary>
internal sealed class NoOpWidgetPlatform : IWidgetPlatform
{
    public bool IsSupported => false;
    public void WriteTimeline(string kind, string json) { }
    public Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken) => Task.CompletedTask;
    public void Reload(string kind) { }
    public void ReloadAll() { }
    public bool AreActivitiesEnabled => false;
    public IReadOnlyDictionary<string, string> ActiveActivities() => ReadOnlyDictionary<string, string>.Empty;
    public Task<string?> StartActivityAsync(string kind, string json, DateTimeOffset? staleAt) => Task.FromResult<string?>(null);
    public void UpdateActivity(string id, string json, DateTimeOffset? staleAt) { }
    public void EndActivity(string id) { }
    public string? PushToStartToken => null;
    public string? PushToken(string id) => null;
    public string? WidgetPushToken => null;
    public void RefreshWidgetPushToken() { }
}
