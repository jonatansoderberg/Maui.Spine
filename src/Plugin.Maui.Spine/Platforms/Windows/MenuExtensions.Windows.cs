using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Plugin.Maui.Spine.Core;
using WButton = Microsoft.UI.Xaml.Controls.Button;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using MenuFlyoutSubItem = Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static readonly ConditionalWeakTable<WButton, MenuObserver> MenuObservers = new();

    static void ConfigureMenus()
    {
        ButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);
        ImageButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);
    }

    static void ApplyMenu(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not WButton button || element is not VisualElement view)
            return;

        if (MenuObservers.TryGetValue(button, out var previous))
        {
            previous.Dispose();
            MenuObservers.Remove(button);
        }

        var items = MenuButton.GetItems(view);
        if (items is null)
        {
            button.Flyout = null;
            return;
        }

        void Build()
        {
            var flyout = new MenuFlyout();
            FillFlyout(handler, view, flyout.Items, items, ref flyout);
            button.Flyout = flyout;
        }

        Build();
        MenuObservers.Add(button, new MenuObserver(items, Build));
    }

    static void FillFlyout(IElementHandler handler, VisualElement owner, IList<MenuFlyoutItemBase> target, IEnumerable<MenuElement> elements, ref MenuFlyout root)
    {
        var pickerCount = 0;

        foreach (var element in elements)
        {
            switch (element)
            {
                case MenuAction action:
                    target.Add(BuildItem(handler, owner, action, null, null));
                    break;

                case MenuSection section:
                    if (target.Count > 0)
                        target.Add(new MenuFlyoutSeparator());
                    if (!string.IsNullOrEmpty(section.Title))
                        target.Add(new MenuFlyoutItem { Text = section.Title, IsEnabled = false });
                    FillFlyout(handler, owner, target, section.Items, ref root);
                    target.Add(new MenuFlyoutSeparator());
                    break;

                case SubMenu subMenu:
                    var sub = new MenuFlyoutSubItem { Text = subMenu.Title, Icon = BuildIcon(handler, subMenu.Svg) };
                    FillFlyout(handler, owner, sub.Items, subMenu.Items, ref root);
                    target.Add(sub);
                    break;

                case MenuPicker picker:
                    var group = $"SpinePicker{pickerCount++}-{picker.GetHashCode()}";
                    foreach (var choice in picker.Items)
                        target.Add(BuildItem(handler, owner, choice, picker, group));
                    break;
            }
        }

        // Two separators in a row, or one at either end, read as a gap; drop them.
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (target[i] is MenuFlyoutSeparator && (i == 0 || i == target.Count - 1 || target[i - 1] is MenuFlyoutSeparator))
                target.RemoveAt(i);
        }
    }

    static MenuFlyoutItemBase BuildItem(IElementHandler handler, VisualElement owner, MenuAction action, MenuPicker? picker, string? group)
    {
        MenuFlyoutItem item = picker is not null
            ? new RadioMenuFlyoutItem { GroupName = group, IsChecked = action.IsChecked }
            : action.KeepsMenuOpen
                ? new ToggleMenuFlyoutItem { IsChecked = action.IsChecked }
                : new MenuFlyoutItem();

        item.Text = action.Title;
        item.IsEnabled = action.IsEnabled;
        item.Icon = BuildIcon(handler, action.Svg);

        if (action.IsDestructive)
            item.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Firebrick);

        item.Click += (_, _) => MenuButton.Pick(owner, action, picker);

        return item;
    }

    static IconElement? BuildIcon(IElementHandler handler, string? svg)
    {
        if (MenuButton.Icon(handler, svg, 16, Colors.Black) is not { } png)
            return null;

        var image = new BitmapImage();
        var icon = new ImageIcon { Source = image };

        _ = SetSourceAsync(image, png);

        return icon;
    }

    static async Task SetSourceAsync(BitmapImage image, byte[] png)
    {
        using var stream = new MemoryStream(png);
        await image.SetSourceAsync(stream.AsRandomAccessStream());
    }
}
