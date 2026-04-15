namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Represents a shortcut that can be surfaced as a platform app-action (dock / jump list)
/// and, on Windows, as an item in the system-tray context menu.
/// </summary>
/// <param name="Id">Stable identifier used to route invocations to <see cref="IShortcutHandler.InvokeAsync"/>.</param>
/// <param name="Title">Human-readable label shown by the platform and in the tray menu.</param>
/// <param name="ShowInTray">
/// When <see langword="true"/> (default) and the Windows tray icon is enabled, this shortcut is
/// also projected as a menu item in the tray context menu.
/// </param>
public sealed record SpineShortcut(string Id, string Title, bool ShowInTray = true);
