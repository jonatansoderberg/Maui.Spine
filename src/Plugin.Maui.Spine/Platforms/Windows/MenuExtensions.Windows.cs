using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Svg;
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
    static readonly ConcurrentDictionary<string, SvgIcon?> MenuIcons = new();

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

        // Filled when it opens rather than here, where the button may not be in a window yet: the
        // icons are rendered for the window's raster scale. Refilled after an item change, or when
        // the window has moved to a display with another scale.
        var flyout = new MenuFlyout();
        double? filledAt = null;
        flyout.Opening += (_, _) =>
        {
            var scale = button.XamlRoot?.RasterizationScale;
            if (filledAt is not null && filledAt == scale)
                return;

            filledAt = scale;
            flyout.Items.Clear();
            FillFlyout(handler, view, flyout.Items, items, ref flyout, null);
        };

        button.Flyout = flyout;
        MenuObservers.Add(button, new MenuObserver(items, () => filledAt = null));
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
        if (string.IsNullOrWhiteSpace(svg) || MenuIcon(services, svg) is not { } icon)
            return null;

        var size = (int)Math.Round(16 * (root?.RasterizationScale ?? DeviceDisplay.MainDisplayInfo.Density));
        try
        {
            // BitmapIcon loads only from a URI.
            return new BitmapIcon { UriSource = new Uri(icon.GetPngFilePath(size)), ShowAsMonochrome = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MenuLogger(services)?.LogError(e, "Menu icon {Svg} at {Size} px could not be written to {Folder}; the item shows without an icon.",
                svg, size, services.GetService<SvgIconOptions>()?.CacheDirectory);
            return null;
        }
    }

    static SvgIcon? MenuIcon(IServiceProvider services, string svg)
    {
        if (services.GetService<ISvgIconService>() is not { } icons)
            return null;

        var name = services.GetService<ResourceNameCache>()?.Resolve(svg) ?? svg;
        return MenuIcons.GetOrAdd(name, static (_, state) =>
        {
            try
            {
                return state.icons.FromEmbeddedSvg(state.svg);
            }
            catch (FileNotFoundException e)
            {
                MenuLogger(state.services)?.LogWarning(e, "Menu icon {Svg} is not embedded; the item shows without an icon.", state.svg);
                return null;
            }
        }, (icons, svg, services));
    }

    static ILogger? MenuLogger(IServiceProvider services) =>
        services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Menus");
}
