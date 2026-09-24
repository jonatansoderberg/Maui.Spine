using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Gives a <see cref="Button"/> or <see cref="ImageButton"/> a native menu that opens on tap:
/// <c>UIButton.Menu</c> on iOS and Mac Catalyst (a glass button morphs into it on iOS 26),
/// a <c>PopupMenu</c> on Android, a <c>MenuFlyout</c> on Windows. A header-bar action takes its
/// menu from <see cref="PageAction.Menu"/> instead.
/// </summary>
/// <remarks>
/// The menu is the button's action: give a menu button no <c>Command</c>. With
/// <see cref="ShowsSelectionProperty"/> the button's <c>Text</c> follows the last picked action,
/// which makes a pop-up button for "Sort by" or "Season" style choices.
/// </remarks>
/// <example>
/// <code>
/// &lt;Button Text="Sort" Glass.Style="Regular" MenuButton.Items="{Binding SortMenu}" MenuButton.ShowsSelection="True" /&gt;
/// </code>
/// </example>
public static class MenuButton
{
    internal const string MapperKey = "SpineMenu";

    /// <summary>Attached property holding the menu.</summary>
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.CreateAttached(
            "Items",
            typeof(MenuItems),
            typeof(MenuButton),
            null,
            propertyChanged: static (bindable, _, _) => (bindable as VisualElement)?.Handler?.UpdateValue(MapperKey));

    /// <summary>Attached property: whether the button's text follows the picked action.</summary>
    public static readonly BindableProperty ShowsSelectionProperty =
        BindableProperty.CreateAttached(
            "ShowsSelection",
            typeof(bool),
            typeof(MenuButton),
            false,
            propertyChanged: static (bindable, _, _) => (bindable as VisualElement)?.Handler?.UpdateValue(MapperKey));

    /// <summary>Gets the menu of <paramref name="view"/>.</summary>
    public static MenuItems? GetItems(BindableObject view) => (MenuItems?)view.GetValue(ItemsProperty);

    /// <summary>Sets the menu of <paramref name="view"/>.</summary>
    public static void SetItems(BindableObject view, MenuItems? value) => view.SetValue(ItemsProperty, value);

    /// <summary>Gets whether the text of <paramref name="view"/> follows the picked action.</summary>
    public static bool GetShowsSelection(BindableObject view) => (bool)view.GetValue(ShowsSelectionProperty);

    /// <summary>Sets whether the text of <paramref name="view"/> follows the picked action.</summary>
    public static void SetShowsSelection(BindableObject view, bool value) => view.SetValue(ShowsSelectionProperty, value);

    /// <summary>
    /// What every platform does when a row is picked: a toggle flips, a picker moves its check and
    /// runs its command, the action's own command runs, and a pop-up button takes the title.
    /// </summary>
    internal static void Pick(VisualElement owner, MenuAction action, MenuPicker? picker)
    {
        if (picker is not null)
        {
            picker.Selected = action;
            var parameter = action.CommandParameter ?? action;
            if (picker.Command?.CanExecute(parameter) == true)
                picker.Command.Execute(parameter);
        }
        else if (action.KeepsMenuOpen)
        {
            action.IsChecked = !action.IsChecked;
        }

        if (action.Command?.CanExecute(action.CommandParameter) == true)
            action.Command.Execute(action.CommandParameter);

        if (GetShowsSelection(owner) && owner is Button button)
            button.Text = action.Title;
    }

    /// <summary>The PNG for a menu icon, at <paramref name="size"/> points, or <see langword="null"/>.</summary>
    internal static byte[]? Icon(IElementHandler handler, string? svg, double size, Color tint)
    {
        if (string.IsNullOrWhiteSpace(svg) || handler.MauiContext?.Services is not { } services)
            return null;

        var names = services.GetService<Svg.ResourceNameCache>();
        var resolved = names?.Resolve(svg) ?? svg;

        if (Svg.SvgBitmapLoader.LoadFromEmbedded(resolved, size, size, tint) is not IStreamImageSource source)
            return null;

        // The rendered PNG is already in memory; the task is complete.
        using var stream = source.GetStreamAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (stream is null)
            return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
