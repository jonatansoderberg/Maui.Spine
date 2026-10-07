using System.Runtime.CompilerServices;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static readonly ConditionalWeakTable<AView, MenuClickListener> MenuListeners = new();

    static void ConfigureMenus()
    {
        ButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);
        ImageButtonHandler.Mapper.AppendToMapping(MenuButton.MapperKey, ApplyMenu);
    }

    static void ApplyMenu(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not AView platformView || element is not VisualElement view)
            return;

        var items = MenuButton.GetItems(view);

        if (MenuListeners.TryGetValue(platformView, out var existing))
        {
            existing.Items = items;
            if (items is null)
            {
                MenuListeners.Remove(platformView);
                platformView.SetOnClickListener(null);
                handler.UpdateValue(nameof(IButton.IsEnabled));
            }
            return;
        }

        if (items is null)
            return;

        // Replaces MAUI's own click listener: the menu is the button's action, and a Command on
        // the same button would otherwise run under the menu.
        var listener = new MenuClickListener(handler, view) { Items = items };
        MenuListeners.Add(platformView, listener);
        platformView.SetOnClickListener(listener);
    }

    sealed class MenuClickListener(IElementHandler handler, VisualElement owner) : Java.Lang.Object, AView.IOnClickListener
    {
        public MenuItems? Items { get; set; }

        public void OnClick(AView? anchor)
        {
            if (Items is not null && anchor is not null)
                ShowPopup(handler, anchor, Items, owner, null);
        }
    }

    /// <summary>
    /// Shows <paramref name="items"/> as a <see cref="PopupMenu"/> anchored to <paramref name="anchor"/>:
    /// a menu button's menu on a tap, a context menu on a long press. A pick runs through
    /// <see cref="MenuButton.Pick"/> with <paramref name="parameter"/> as the fallback parameter.
    /// </summary>
    // A destructive row is red, its icon too, as UIKit draws it.
    static readonly Color DestructiveColor = Color.FromRgb(211, 47, 47);

    internal static PopupMenu? ShowPopup(IElementHandler handler, AView anchor, MenuItems items, VisualElement owner, object? parameter)
    {
        if (anchor.Context is not { } context)
            return null;

        var popup = new PopupMenu(context, anchor);
        var actions = new Dictionary<int, (MenuAction Action, MenuPicker? Picker)>();
        var nextGroup = 1;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            popup.SetForceShowIcon(true);
        var menu = popup.Menu!;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            menu.SetGroupDividerEnabled(true);

        FillPopup(handler, menu, items, 0, ref nextGroup, actions);

        popup.MenuItemClick += (_, e) =>
        {
            if (e.Item is { } item && actions.TryGetValue(item.ItemId, out var picked))
                MenuButton.Pick(owner, picked.Action, picked.Picker, parameter);
        };

        popup.Show();
        return popup;
    }

    static void FillPopup(IElementHandler handler, IMenu menu, IEnumerable<MenuElement> elements, int group, ref int nextGroup, Dictionary<int, (MenuAction, MenuPicker?)> actions)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case MenuAction { IsVisible: true } action:
                    AddPopupAction(handler, menu, action, null, group, actions);
                    break;

                case MenuSection section:
                    var sectionGroup = nextGroup++;
                    if (!string.IsNullOrEmpty(section.Title))
                        menu.Add(sectionGroup, AView.GenerateViewId(), IMenu.None, section.Title)!.SetEnabled(false);
                    FillPopup(handler, menu, section.Items, sectionGroup, ref nextGroup, actions);
                    break;

                case SubMenu { IsVisible: true } subMenu:
                    var sub = menu.AddSubMenu(group, AView.GenerateViewId(), IMenu.None, subMenu.Title)!;
                    if (PopupIcon(handler, subMenu.Svg) is { } icon)
                        sub.SetIcon(icon);
                    FillPopup(handler, sub, subMenu.Items, 0, ref nextGroup, actions);
                    break;

                case MenuPicker picker:
                    var pickerGroup = nextGroup++;
                    foreach (var choice in picker.Items.Where(a => a.IsVisible))
                        AddPopupAction(handler, menu, choice, picker, pickerGroup, actions);
                    menu.SetGroupCheckable(pickerGroup, true, true);
                    break;
            }
        }
    }

    static void AddPopupAction(IElementHandler handler, IMenu menu, MenuAction action, MenuPicker? picker, int group, Dictionary<int, (MenuAction, MenuPicker?)> actions)
    {
        var id = AView.GenerateViewId();
        var item = menu.Add(group, id, IMenu.None, PopupTitle(action))!;

        item.SetEnabled(action.IsEnabled);

        if (picker is not null || action.KeepsMenuOpen)
            item.SetCheckable(true).SetChecked(action.IsChecked);

        if (PopupIcon(handler, action.Svg, action.IsDestructive ? DestructiveColor : null) is { } icon)
            item.SetIcon(icon);

        actions[id] = (action, picker);
    }

    static Java.Lang.ICharSequence PopupTitle(MenuAction action)
    {
        if (!action.IsDestructive)
            return new Java.Lang.String(action.Title);

        var text = new SpannableString(action.Title);
        text.SetSpan(new ForegroundColorSpan(DestructiveColor.ToPlatform()), 0, action.Title.Length, SpanTypes.ExclusiveExclusive);
        return text;
    }

    static Drawable? PopupIcon(IElementHandler handler, string? svg, Color? tint = null)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        if (MenuButton.Icon(handler, svg, 24, tint ?? (dark ? Colors.White : Colors.Black)) is not { } png)
            return null;

        var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(png, 0, png.Length);
        return bitmap is null ? null : new BitmapDrawable(handler.MauiContext?.Context?.Resources, bitmap);
    }
}
