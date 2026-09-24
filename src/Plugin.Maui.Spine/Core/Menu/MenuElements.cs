using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Plugin.Maui.Spine.Core;

/// <summary>An entry in a menu: an action, a section, a submenu or a picker.</summary>
public abstract class MenuElement : ObservableObject
{
}

/// <summary>The items a menu button shows, in order. Change it and the native menu follows.</summary>
public sealed class MenuItems : ObservableCollection<MenuElement>
{
    /// <summary>An empty menu.</summary>
    public MenuItems() { }

    /// <summary>A menu with <paramref name="items"/>.</summary>
    public MenuItems(IEnumerable<MenuElement> items) : base(items) { }
}

/// <summary>One tappable row.</summary>
public sealed partial class MenuAction : MenuElement
{
    /// <summary>A row with a title and an optional SVG icon.</summary>
    public MenuAction(string title, string? svg = null)
    {
        Title = title;
        Svg = svg;
    }

    /// <summary>A row that runs <paramref name="command"/> when picked.</summary>
    public MenuAction(string title, string? svg, ICommand command) : this(title, svg)
    {
        Command = command;
    }

    /// <summary>The row's text.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>An SVG resource name, e.g. <c>"check.svg"</c>, shown beside the title.</summary>
    [ObservableProperty]
    public partial string? Svg { get; set; }

    /// <summary>Run when the row is picked, with <see cref="CommandParameter"/>.</summary>
    [ObservableProperty]
    public partial ICommand? Command { get; set; }

    /// <summary>Passed to <see cref="Command"/>, and to a <see cref="MenuPicker"/>'s command in place of the action.</summary>
    [ObservableProperty]
    public partial object? CommandParameter { get; set; }

    /// <summary>Shows a checkmark. Toggled by a pick when <see cref="KeepsMenuOpen"/> is on; set by the picker for its items.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>Whether the row can be picked.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>Styled as destructive (red) on platforms that have the notion.</summary>
    [ObservableProperty]
    public partial bool IsDestructive { get; set; }

    /// <summary>
    /// A toggle: the pick flips <see cref="IsChecked"/> and the menu stays open on iOS 16+ and Windows.
    /// Android closes the menu on every pick.
    /// </summary>
    [ObservableProperty]
    public partial bool KeepsMenuOpen { get; set; }
}

/// <summary>A group rendered inline, separated from its neighbours, with an optional title.</summary>
public sealed partial class MenuSection : MenuElement, IEnumerable<MenuElement>
{
    /// <summary>A section without a title.</summary>
    public MenuSection() { }

    /// <summary>A section titled <paramref name="title"/>.</summary>
    public MenuSection(string? title)
    {
        Title = title;
    }

    /// <summary>The section's heading, or <see langword="null"/> for a plain separator group.</summary>
    [ObservableProperty]
    public partial string? Title { get; set; }

    /// <summary>The section's entries.</summary>
    public ObservableCollection<MenuElement> Items { get; } = [];

    /// <summary>Collection-initializer support.</summary>
    public void Add(MenuElement element) => Items.Add(element);

    /// <inheritdoc/>
    public IEnumerator<MenuElement> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A row that opens a nested menu.</summary>
public sealed partial class SubMenu : MenuElement, IEnumerable<MenuElement>
{
    /// <summary>A submenu titled <paramref name="title"/> with an optional SVG icon.</summary>
    public SubMenu(string title, string? svg = null)
    {
        Title = title;
        Svg = svg;
    }

    /// <summary>The row's text.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>An SVG resource name shown beside the title.</summary>
    [ObservableProperty]
    public partial string? Svg { get; set; }

    /// <summary>The nested menu's entries.</summary>
    public ObservableCollection<MenuElement> Items { get; } = [];

    /// <summary>Collection-initializer support.</summary>
    public void Add(MenuElement element) => Items.Add(element);

    /// <inheritdoc/>
    public IEnumerator<MenuElement> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A single-selection group: exactly one of its actions is checked, and a pick moves the check,
/// sets <see cref="Selected"/> and runs <see cref="Command"/> with the picked action's
/// <see cref="MenuAction.CommandParameter"/>, or the action itself when that is null.
/// </summary>
public sealed partial class MenuPicker : MenuElement, IEnumerable<MenuAction>
{
    /// <summary>A picker with no command; read <see cref="Selected"/> instead.</summary>
    public MenuPicker() { }

    /// <summary>A picker that runs <paramref name="command"/> on every pick.</summary>
    public MenuPicker(ICommand command)
    {
        Command = command;
    }

    /// <summary>Run on every pick.</summary>
    [ObservableProperty]
    public partial ICommand? Command { get; set; }

    /// <summary>The checked action. Setting it moves the checkmark.</summary>
    [ObservableProperty]
    public partial MenuAction? Selected { get; set; }

    /// <summary>The choices.</summary>
    public ObservableCollection<MenuAction> Items { get; } = [];

    /// <summary>Collection-initializer support. The first action added with <see cref="MenuAction.IsChecked"/> becomes <see cref="Selected"/>.</summary>
    public void Add(MenuAction action)
    {
        Items.Add(action);

        if (action.IsChecked && Selected is null)
            Selected = action;
    }

    partial void OnSelectedChanged(MenuAction? value)
    {
        foreach (var item in Items)
            item.IsChecked = ReferenceEquals(item, value);
    }

    /// <inheritdoc/>
    public IEnumerator<MenuAction> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
