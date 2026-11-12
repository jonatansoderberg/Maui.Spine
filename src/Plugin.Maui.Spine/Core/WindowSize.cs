namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A window size a page suggests for its content (see <see cref="ViewModelBase.SuggestWindowSizes"/>), offered in a
/// menu: when the header bar is right-clicked on the Mac, in the window's system menu on Windows (a right-click on the
/// title row, or Alt+Space).
/// </summary>
/// <param name="Title">The menu item's text, such as "Two columns".</param>
/// <param name="Size">The window's content size in the app's points; it is clamped to the screen.</param>
/// <param name="SystemImage">An SF Symbol for the item, such as <c>rectangle.split.2x1</c>, or <see langword="null"/>. The Mac
/// shows it; the Windows system menu has text only.</param>
public sealed record WindowSize(string Title, Size Size, string? SystemImage = null)
{
    // How far a window may be from a size and still count as that size, in points: rounding to the display's pixels.
    const double Slack = 2;

    /// <summary>
    /// A command such as "Fit to content" rather than a size to pick: it is never checked, it comes after the sizes behind
    /// a separator, and the window's own fitting (<see cref="ViewModelBase.FitWindowToContent"/>, at launch) leaves it out.
    /// </summary>
    /// <example>
    /// <code>
    /// sizes.Add(new WindowSize("Fit to content", current.Size, "arrow.down.right.and.arrow.up.left") { IsCommand = true });
    /// </code>
    /// </example>
    public bool IsCommand { get; init; }

    /// <summary>
    /// The suggestion for a window <paramref name="width"/> points wide: the widest no wider than it, or the narrowest.
    /// Both platforms fit the window with it (at launch and for <see cref="ViewModelBase.FitWindowToContent"/>).
    /// </summary>
    internal static WindowSize ForWidth(IReadOnlyList<WindowSize> sizes, double width)
    {
        // The sizes to pick from; commands only when the page offers nothing else.
        var choices = sizes.Where(s => !s.IsCommand).ToList() is { Count: > 0 } picked ? picked : sizes.ToList();
        return choices.Where(s => s.Size.Width <= width + Slack).MaxBy(s => s.Size.Width) ?? choices.MinBy(s => s.Size.Width)!;
    }

    /// <summary>
    /// Whether a window of size <paramref name="current"/> should take this size: at launch only to grow taller
    /// (<paramref name="onlyGrow"/>), otherwise whenever it differs.
    /// </summary>
    internal bool Changes(Size current, bool onlyGrow) => onlyGrow
        ? Size.Height > current.Height + Slack
        : !Matches(current);

    /// <summary>Whether a window of size <paramref name="current"/> has this size: the menu checks that item.</summary>
    internal bool Matches(Size current) =>
        Math.Abs(current.Width - Size.Width) < Slack && Math.Abs(current.Height - Size.Height) < Slack;
}
