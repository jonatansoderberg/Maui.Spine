#if IOS || MACCATALYST

using System.Runtime.CompilerServices;
using Foundation;
using Microsoft.Maui.Handlers;
using Plugin.Maui.Spine.Core;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static readonly ConditionalWeakTable<UIButton, MenuObserver> MenuObservers = new();

    const double PopupIndicatorWidth = 22;

    static readonly BindableProperty PopupPaddingAppliedProperty =
        BindableProperty.CreateAttached("PopupPaddingApplied", typeof(bool), typeof(SpineExtensions), false);

    static void ConfigureMenus()
    {
        ButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);
        ImageButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);

        // MAUI's padding mapper writes the insets back; a plain pop-up button needs its room again.
        ButtonHandler.Mapper.AppendToMapping(nameof(IPadding.Padding), static (handler, element) =>
        {
            if (handler.PlatformView is UIButton { Configuration: null } button && element is Button view
                && view.IsSet(PopupPaddingAppliedProperty))
            {
                var insets = button.ContentEdgeInsets;
                button.ContentEdgeInsets = new UIEdgeInsets(insets.Top, insets.Left, insets.Bottom, (nfloat)PopupTrailingInset(view, insets.Right));
            }
        });
    }

    /// <summary>
    /// The trailing inset a button should lay out with: the reserved chevron width is part of the
    /// frame, not of the content insets, or the title loses what the indicator gained.
    /// </summary>
    static double PopupTrailingInset(Button view, double right) =>
        view.IsSet(PopupPaddingAppliedProperty) ? Math.Max(0, right - PopupIndicatorWidth) : right;

    static void ApplyMenu(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not UIButton button || element is not VisualElement view)
            return;

        if (MenuObservers.TryGetValue(button, out var previous))
        {
            previous.Dispose();
            MenuObservers.Remove(button);
        }

        var items = MenuButton.GetItems(view);
        if (items is null)
        {
            button.Menu = null;
            button.ShowsMenuAsPrimaryAction = false;
            return;
        }

        void Build()
        {
            button.Menu = BuildMenu(handler, view, items);
            button.ShowsMenuAsPrimaryAction = true;

            if (OperatingSystem.IsIOSVersionAtLeast(15) || OperatingSystem.IsMacCatalystVersionAtLeast(15))
                button.ChangesSelectionAsPrimaryAction = MenuButton.GetShowsSelection(view);
        }

        // MAUI measures a Button as its title plus Padding; the pop-up chevron UIKit adds takes
        // room the measurement knows nothing about, so the title wrapped. Reserve it once.
        if (MenuButton.GetShowsSelection(view) && view is Button popup && !popup.IsSet(PopupPaddingAppliedProperty))
        {
            var padding = popup.Padding.IsNaN ? ButtonHandler.DefaultPadding : popup.Padding;
            popup.SetValue(PopupPaddingAppliedProperty, true);
            popup.Padding = new Thickness(padding.Left, padding.Top, padding.Right + PopupIndicatorWidth, padding.Bottom);
        }

        Build();
        MenuObservers.Add(button, new MenuObserver(items, Build));
    }

    static UIMenu BuildMenu(IElementHandler handler, VisualElement owner, IEnumerable<MenuElement> elements) =>
        UIMenu.Create(string.Empty, null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, BuildChildren(handler, owner, elements));

    static UIMenuElement[] BuildChildren(IElementHandler handler, VisualElement owner, IEnumerable<MenuElement> elements)
    {
        var children = new List<UIMenuElement>();

        foreach (var element in elements)
        {
            switch (element)
            {
                case MenuAction action:
                    children.Add(BuildAction(handler, owner, action, null));
                    break;

                case MenuSection section:
                    children.Add(UIMenu.Create(section.Title ?? string.Empty, null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline,
                        BuildChildren(handler, owner, section.Items)));
                    break;

                case SubMenu subMenu:
                    children.Add(UIMenu.Create(subMenu.Title, MenuImage(handler, subMenu.Svg), UIMenuIdentifier.None, 0,
                        BuildChildren(handler, owner, subMenu.Items)));
                    break;

                case MenuPicker picker:
                    var options = UIMenuOptions.DisplayInline;
                    if (OperatingSystem.IsIOSVersionAtLeast(15) || OperatingSystem.IsMacCatalystVersionAtLeast(15))
                        options |= UIMenuOptions.SingleSelection;

                    children.Add(UIMenu.Create(string.Empty, null, UIMenuIdentifier.None, options,
                        [.. picker.Items.Select(a => BuildAction(handler, owner, a, picker))]));
                    break;
            }
        }

        return [.. children];
    }

    static UIAction BuildAction(IElementHandler handler, VisualElement owner, MenuAction action, MenuPicker? picker)
    {
        var uiAction = UIAction.Create(action.Title, MenuImage(handler, action.Svg), null, _ => MenuButton.Pick(owner, action, picker));

        uiAction.State = action.IsChecked ? UIMenuElementState.On : UIMenuElementState.Off;

        var attributes = (UIMenuElementAttributes)0;
        if (action.IsDestructive)
            attributes |= UIMenuElementAttributes.Destructive;
        if (!action.IsEnabled)
            attributes |= UIMenuElementAttributes.Disabled;
        if (action.KeepsMenuOpen && (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16)))
            attributes |= UIMenuElementAttributes.KeepsMenuPresented;
        uiAction.Attributes = attributes;

        return uiAction;
    }

    static UIImage? MenuImage(IElementHandler handler, string? svg)
    {
        if (MenuButton.Icon(handler, svg, 20, Colors.Black) is not { } png)
            return null;

        using var data = NSData.FromArray(png);
        return UIImage.LoadFromData(data, UIScreen.MainScreen.Scale)?.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
    }
}

#endif
