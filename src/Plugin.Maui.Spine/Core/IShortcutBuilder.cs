namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Fluent builder used inside <see cref="IShortcutHandler.Configure"/> to declare the
/// shortcuts that Spine should register with the platform.
/// </summary>
public interface IShortcutBuilder
{
    /// <summary>Adds a shortcut with the given <paramref name="id"/> and <paramref name="title"/>.</summary>
    /// <param name="id">Stable identifier used to route invocations to <see cref="IShortcutHandler.InvokeAsync"/>.</param>
    /// <param name="title">Human-readable label shown by the platform and in the tray menu.</param>
    /// <param name="showInTray">
    /// When <c>true</c> (default) and the Windows tray icon is enabled, this shortcut is also
    /// projected as a menu item in the tray context menu.
    /// </param>
    IShortcutBuilder Add(string id, string title, bool showInTray = true);
}
