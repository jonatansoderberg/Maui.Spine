using System.Text.Json;
using System.Text.Json.Serialization;
using Orientera.Services.Sources;

namespace Orientera.Services.Notifications;

/// <summary>
/// Which notification types the user has said yes to. Opt-in per type, per the requirements —
/// and off by default, because an app that starts notifying before being asked is one people
/// turn off entirely.
/// </summary>
public sealed record NotificationPreferences
{
    public static readonly NotificationPreferences Default = new();

    /// <summary>Which types are on.</summary>
    /// <remarks>
    /// A <see cref="HashSet{T}"/> and not <c>IReadOnlySet</c>: System.Text.Json cannot construct a
    /// read-only interface, and the store below caught the failure and fell back to "nothing
    /// enabled". Every launch after the first therefore forgot what the user had turned on, the
    /// switches read as off, and the phone registered for no push at all.
    /// </remarks>
    public HashSet<NotificationKind> Enabled { get; init; } = [];

    public bool IsEnabled(NotificationKind kind) => Enabled.Contains(kind);

    [JsonIgnore]
    public bool Any => Enabled.Count > 0;

    public NotificationPreferences With(NotificationKind kind, bool enabled)
    {
        var next = new HashSet<NotificationKind>(Enabled);

        if (enabled)
            next.Add(kind);
        else
            next.Remove(kind);

        return this with { Enabled = next };
    }
}

/// <summary>The preferences, kept on the phone next to the rest of what is local.</summary>
public sealed class NotificationPreferencesStore(string _path)
{
    private NotificationPreferences? _preferences;

    public NotificationPreferences Current
    {
        get
        {
            if (_preferences is not null)
                return _preferences;

            try
            {
                if (File.Exists(_path))
                    _preferences = JsonSerializer.Deserialize<NotificationPreferences>(
                        File.ReadAllText(_path), OrienteraJson.Options);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                // A file that is missing or half-written is a phone that has not been asked yet.
                // Anything else — a type the serializer cannot build, say — is a bug in this app,
                // and swallowing it is what hid exactly that for as long as it did.
                _preferences = null;
            }

            return _preferences ??= NotificationPreferences.Default;
        }
    }

    public void Save(NotificationPreferences preferences)
    {
        _preferences = preferences;
        File.WriteAllText(_path, JsonSerializer.Serialize(preferences, OrienteraJson.Options));
    }
}
