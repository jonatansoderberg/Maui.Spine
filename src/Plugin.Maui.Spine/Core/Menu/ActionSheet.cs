namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A list of actions the platform shows in its own action sheet, opened with
/// <see cref="INavigationService.ShowActionsAsync"/>: a <c>UIAlertController</c> action sheet on
/// iOS and Mac Catalyst (a popover on iPad), a Material bottom sheet on Android and a
/// <c>MenuFlyout</c> on Windows.
/// </summary>
/// <remarks>
/// The rows are the same <see cref="MenuAction"/> as in a menu: <see cref="MenuAction.Title"/>,
/// <see cref="MenuAction.Svg"/>, <see cref="MenuAction.Command"/>, <see cref="MenuAction.CommandParameter"/>,
/// <see cref="MenuAction.IsDestructive"/>, <see cref="MenuAction.IsEnabled"/> and
/// <see cref="MenuAction.IsVisible"/> apply. <see cref="MenuAction.IsChecked"/> and
/// <see cref="MenuAction.KeepsMenuOpen"/> belong to menus and are not shown. A sheet is one flat list,
/// so it takes no sections, submenus or pickers.
/// </remarks>
/// <example>
/// <code>
/// var picked = await _navigation.ShowActionsAsync(new ActionSheet("Night sprint")
/// {
///     Actions =
///     [
///         new("Share", SpineIcons.Share, ShareCommand),
///         new("Remove", SpineIcons.Trashcan, RemoveCommand) { IsDestructive = true },
///     ],
/// });
/// </code>
/// </example>
public sealed class ActionSheet
{
    /// <summary>A sheet without a title.</summary>
    public ActionSheet() { }

    /// <summary>A sheet titled <paramref name="title"/>, with an optional <paramref name="message"/> under it.</summary>
    public ActionSheet(string? title, string? message = null)
    {
        Title = title;
        Message = message;
    }

    /// <summary>The line above the actions, or <see langword="null"/> for none.</summary>
    public string? Title { get; set; }

    /// <summary>A smaller line under the title, or <see langword="null"/> for none.</summary>
    public string? Message { get; set; }

    /// <summary>The rows, in order. A row with <see cref="MenuAction.IsVisible"/> off is left out.</summary>
    public IList<MenuAction> Actions { get; init; } = [];

    /// <summary>
    /// The Cancel row's text on iOS and Mac Catalyst, where the sheet has one; <see langword="null"/>
    /// is the localised <c>Spine.Header.Cancel</c>. A sheet grown out of its anchor, iPad's popover,
    /// Android and Windows cancel with a tap outside the sheet instead.
    /// </summary>
    public string? CancelText { get; set; }

    /// <summary>
    /// Passed to a picked row's command when the row has no <see cref="MenuAction.CommandParameter"/>
    /// of its own, so one set of rows can act on whichever item the sheet was opened for.
    /// </summary>
    public object? CommandParameter { get; set; }

    /// <summary>The rows the sheet shows.</summary>
    internal IReadOnlyList<MenuAction> VisibleActions => [.. Actions.Where(static a => a.IsVisible)];

    /// <summary>What every platform does with a pick: runs the row's command with its parameter, or the sheet's.</summary>
    internal void Pick(MenuAction action)
    {
        var parameter = action.CommandParameter ?? CommandParameter;
        if (action.Command?.CanExecute(parameter) == true)
            action.Command.Execute(parameter);
    }
}
