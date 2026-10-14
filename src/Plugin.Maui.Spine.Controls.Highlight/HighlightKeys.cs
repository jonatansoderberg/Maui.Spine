namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The keys of highlights that have been seen, in <see cref="Preferences"/> under a shared name of their
/// own, so forgetting them all clears nothing else.
/// </summary>
/// <remarks>
/// iOS deletes them with the app. On Android, Auto Backup restores them after a reinstall when the app
/// allows backup, so a hint can stay seen for the same Google account.
/// </remarks>
internal static class HighlightKeys
{
    public const string SharedName = "spine.highlight";

    static readonly WeakEventManager ResetEvents = new();

    /// <summary>Raised after keys were forgotten, so highlights waiting on them can play again. Held weakly.</summary>
    public static event EventHandler WasReset
    {
        add => ResetEvents.AddEventHandler(value);
        remove => ResetEvents.RemoveEventHandler(value);
    }

    public static bool HasSeen(string key) => Preferences.Default.Get(key, false, SharedName);

    public static void MarkSeen(string key) => Preferences.Default.Set(key, true, SharedName);

    public static void Reset(string? key)
    {
        if (key is null)
            Preferences.Default.Clear(SharedName);
        else
            Preferences.Default.Remove(key, SharedName);

        ResetEvents.HandleEvent(null!, EventArgs.Empty, nameof(WasReset));
    }
}
