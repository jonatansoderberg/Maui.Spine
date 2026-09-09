using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// The plan as this device last had it, kept across processes.
/// </summary>
/// <remarks>
/// Android has no way to ask <c>AlarmManager</c> what is booked, and a <c>PendingIntent</c> can only
/// be cancelled by rebuilding an equal one — so an alarm set by an earlier run of the app is
/// unreachable unless its id was written down. This is that. It is also what
/// <see cref="ILocalNotificationService.PendingAsync"/> answers from, and what the boot receiver
/// replays after a restart clears the alarms.
/// </remarks>
internal static class LocalNotificationStore
{
    private const string Key = "spine.push.local.plan";

    internal static IReadOnlyList<LocalNotification> Read()
    {
        var json = Preferences.Default.Get(Key, "");
        if (json is not { Length: > 0 }) return [];

        try
        {
            return JsonSerializer.Deserialize(json, LocalNotificationJsonContext.Default.LocalNotificationArray) ?? [];
        }
        catch (JsonException)
        {
            // A plan written by an older version that no longer parses is worth less than a working
            // app: drop it and let the next Sync write a whole one.
            Preferences.Default.Remove(Key);
            return [];
        }
    }

    internal static void Write(IReadOnlyList<LocalNotification> plan) =>
        Preferences.Default.Set(Key, JsonSerializer.Serialize(
            plan as LocalNotification[] ?? [.. plan], LocalNotificationJsonContext.Default.LocalNotificationArray));

    /// <summary>Drops one that has fired, so the stored plan keeps meaning "still to come".</summary>
    internal static void Remove(string id) =>
        Write([.. Read().Where(n => n.Id != id)]);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LocalNotification[]))]
internal sealed partial class LocalNotificationJsonContext : JsonSerializerContext;
