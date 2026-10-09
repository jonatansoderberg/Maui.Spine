using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Plugin.Maui.Spine.Core;
using WButton = Microsoft.UI.Xaml.Controls.Button;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using MenuFlyoutSubItem = Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using UIElement = Microsoft.UI.Xaml.UIElement;
using XamlRoot = Microsoft.UI.Xaml.XamlRoot;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static readonly ConditionalWeakTable<WButton, MenuObserver> MenuObservers = new();
    static readonly ConcurrentDictionary<string, string> MenuIconFiles = new();

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
            FillFlyout(handler, view, flyout.Items, items, ref flyout, null);
            button.Flyout = flyout;
        }

        Build();
        MenuObservers.Add(button, new MenuObserver(items, Build));
    }

    static void FillFlyout(IElementHandler handler, VisualElement owner, IList<MenuFlyoutItemBase> target, IEnumerable<MenuElement> elements, ref MenuFlyout root, object? parameter)
    {
        var pickerCount = 0;

        foreach (var element in elements)
        {
            switch (element)
            {
                case MenuAction { IsVisible: true } action:
                    target.Add(BuildItem(handler, owner, action, null, null, parameter));
                    break;

                case MenuSection section:
                    if (target.Count > 0)
                        target.Add(new MenuFlyoutSeparator());
                    if (!string.IsNullOrEmpty(section.Title))
                        target.Add(new MenuFlyoutItem { Text = section.Title, IsEnabled = false });
                    FillFlyout(handler, owner, target, section.Items, ref root, parameter);
                    target.Add(new MenuFlyoutSeparator());
                    break;

                case SubMenu { IsVisible: true } subMenu:
                    var sub = new MenuFlyoutSubItem { Text = subMenu.Title, Icon = BuildIcon(handler, subMenu.Svg) };
                    FillFlyout(handler, owner, sub.Items, subMenu.Items, ref root, parameter);
                    target.Add(sub);
                    break;

                case MenuPicker picker:
                    var group = $"SpinePicker{pickerCount++}-{picker.GetHashCode()}";
                    foreach (var choice in picker.Items.Where(a => a.IsVisible))
                        target.Add(BuildItem(handler, owner, choice, picker, group, parameter));
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

    static MenuFlyoutItemBase BuildItem(IElementHandler handler, VisualElement owner, MenuAction action, MenuPicker? picker, string? group, object? parameter)
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

        item.Click += (_, _) => MenuButton.Pick(owner, action, picker, parameter);

        return item;
    }

    static IconElement? BuildIcon(IElementHandler handler, string? svg) =>
        handler.MauiContext?.Services is { } services ? BuildIcon(services, svg, (handler.PlatformView as UIElement)?.XamlRoot) : null;

    // A monochrome BitmapIcon keeps only the alpha and fills it with the item's foreground, so the
    // icon follows the theme, a destructive item's colour, the disabled state and a theme switch.
    // The template's 16 × 16 Viewbox scales the bitmap, which is rendered for the display's scale.
    internal static IconElement? BuildIcon(IServiceProvider services, string? svg, XamlRoot? root)
    {
        var scale = root?.RasterizationScale ?? DeviceDisplay.MainDisplayInfo.Density;
        if (MenuButton.Icon(services, svg, 16 * scale, Colors.Black) is not { } png)
            return null;

        return new BitmapIcon { UriSource = new Uri(MenuIconFile(png)), ShowAsMonochrome = true };
    }

    // BitmapIcon loads only from a URI, so each PNG is written once, named after its contents.
    static string MenuIconFile(byte[] png) =>
        MenuIconFiles.GetOrAdd(Convert.ToHexString(SHA256.HashData(png))[..32], static (name, png) =>
        {
            var folder = Path.Combine(FileSystem.CacheDirectory, "spine-menu-icons");
            var path = Path.Combine(folder, $"{name}.png");
            if (File.Exists(path))
                return path;

            Directory.CreateDirectory(folder);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporary, png);
            try
            {
                File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temporary);
            }

            return path;
        }, png);
}
