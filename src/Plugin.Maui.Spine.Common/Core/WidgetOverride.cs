using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>The platforms an override applies on.</summary>
[Flags]
public enum WidgetPlatform
{
    /// <summary>iOS and iPadOS.</summary>
    iOS = 1,
    /// <summary>Android.</summary>
    Android = 2,
}

/// <summary>Where a tree is drawn, for an override that applies there.</summary>
[Flags]
public enum WidgetPlace
{
    /// <summary>A home-screen widget.</summary>
    HomeScreen = 1,
    /// <summary>A Lock Screen widget, or a Live Activity on the iOS Lock Screen.</summary>
    LockScreen = 2,
    /// <summary>The Dynamic Island: its expanded, compact and minimal regions.</summary>
    Island = 4,
    /// <summary>A Live Activity's Android notification.</summary>
    Notification = 8,
    /// <summary>The Android status-bar chip of a promoted Live Update.</summary>
    StatusChip = 16,
    /// <summary>CarPlay's Dashboard (a Live Activity's <c>Small</c>).</summary>
    CarPlay = 32,
    /// <summary>The Apple Watch Smart Stack (a Live Activity's <c>Small</c>).</summary>
    Watch = 64,
}

/// <summary>
/// One override of a node: where it applies and what it sets there, as a JSON merge patch of the node's own properties.
/// Written under the node's <c>on</c> key; renderers apply the ones that match where they draw, in order, the last one
/// winning, and an older renderer ignores the key. Built with <see cref="WidgetNodeStyling.On{T}(T, WidgetPlatform, Func{T, T})"/>.
/// </summary>
public sealed record WidgetOverride
{
    /// <summary>Where it applies; every condition set must hold.</summary>
    [JsonPropertyName("when")]
    public required WidgetOverrideWhen When { get; init; }

    /// <summary>The properties it sets; a <c>null</c> value takes one away.</summary>
    [JsonPropertyName("set")]
    public required JsonObject Set { get; init; }
}

/// <summary>The conditions of a <see cref="WidgetOverride"/>, as their JSON names, a comma between values meaning either.</summary>
public sealed record WidgetOverrideWhen
{
    /// <summary><c>ios</c>, <c>android</c>.</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    /// <summary><c>homeScreen</c>, <c>lockScreen</c>, <c>island</c>, <c>notification</c>, <c>statusChip</c>, <c>carPlay</c>, <c>watch</c>.</summary>
    [JsonPropertyName("surface")]
    public string? Surface { get; init; }

    /// <summary>Widget families by their JSON names: <c>small</c>, <c>medium</c>, <c>accessoryCircular</c>…</summary>
    [JsonPropertyName("size")]
    public string? Size { get; init; }

    /// <summary>The lowest major OS version it applies on: 18 for something iOS 18 brought.</summary>
    [JsonPropertyName("minVersion")]
    public int? MinVersion { get; init; }

    internal static string? Names<TEnum>(TEnum? flags) where TEnum : struct, Enum =>
        flags is not { } value ? null : string.Join(',', Enum.GetValues<TEnum>().Where(f => value.HasFlag(f)).Select(f => JsonName(f.ToString())));

    internal static string JsonName(string name) => name == "iOS" ? "ios" : char.ToLowerInvariant(name[0]) + name[1..];
}
