namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Represents a shortcut that can be surfaced as a platform app-action (dock / jump list)
/// and, on Windows, as an item in the system-tray context menu.
/// </summary>
/// <param name="Id">Stable identifier used to route invocations to <see cref="IShortcutHandler.InvokeAsync"/>.</param>
/// <param name="Title">
/// Label shown by the platform and in the tray menu. A key in the string store (<c>"Menu.Show"</c>) is looked up when the
/// menu is built and again when the culture changes: <c>Configure</c> runs inside <c>UseSpine</c>, before the app's
/// string documents are loaded, so the handler cannot look it up itself.
/// </param>
/// <param name="ShowInTray">
/// When <see langword="true"/> (default) and the Windows tray icon is enabled, this shortcut is
/// also projected as a menu item in the tray context menu.
/// </param>
/// <param name="Icon">
/// An SVG by name (<c>qrcode</c>, <c>figure.run</c>) shown beside the title. The home screen draws it,
/// so the build renders it into the app: declare it with <c>&lt;SpineShortcutIcon Include="qrcode" /&gt;</c>.
/// Without that, iOS shows the SF Symbol of the same name if there is one.
/// </param>
/// <param name="Subtitle">A second line under the title on iOS; the long label on Android.</param>
public sealed record SpineShortcut(string Id, string Title, bool ShowInTray = true, string? Icon = null, string? Subtitle = null)
{
    /// <summary><see cref="Title"/>, or its text when it is a key in the string store.</summary>
    internal string DisplayTitle => Common.SpineStrings.Current.Has(Title) ? Common.SpineStrings.Current[Title] : Title;
}
