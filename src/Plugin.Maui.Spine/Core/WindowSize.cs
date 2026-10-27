namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A window size a page suggests for its content, offered in a menu when the header bar is right-clicked on the Mac
/// (see <see cref="ViewModelBase.SuggestWindowSizes"/>), as snap layouts are offered on Windows.
/// </summary>
/// <param name="Title">The menu item's text, such as "Two columns".</param>
/// <param name="Size">The window's content size in the app's points; it is clamped to the screen.</param>
/// <param name="SystemImage">An SF Symbol for the item, such as <c>rectangle.split.2x1</c>, or <see langword="null"/>.</param>
public sealed record WindowSize(string Title, Size Size, string? SystemImage = null);
