using System.Runtime.CompilerServices;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
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
            if (Items is null || anchor?.Context is not { } context)
                return;

            var popup = new PopupMenu(context, anchor);
            var actions = new Dictionary<int, (MenuAction Action, MenuPicker? Picker)>();
            var nextGroup = 1;

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                popup.SetForceShowIcon(true);
            var menu = popup.Menu!;
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                menu.SetGroupDividerEnabled(true);

            Fill(menu, Items, 0, ref nextGroup, actions);

            popup.MenuItemClick += (_, e) =>
            {
                if (e.Item is { } item && actions.TryGetValue(item.ItemId, out var picked))
                    MenuButton.Pick(owner, picked.Action, picked.Picker);
            };

            popup.Show();
        }

        void Fill(IMenu menu, IEnumerable<MenuElement> elements, int group, ref int nextGroup, Dictionary<int, (MenuAction, MenuPicker?)> actions)
        {
            foreach (var element in elements)
            {
                switch (element)
                {
                    case MenuAction action:
                        AddAction(menu, action, null, group, actions);
                        break;

                    case MenuSection section:
                        var sectionGroup = nextGroup++;
                        if (!string.IsNullOrEmpty(section.Title))
                            menu.Add(sectionGroup, AView.GenerateViewId(), IMenu.None, section.Title)!.SetEnabled(false);
                        Fill(menu, section.Items, sectionGroup, ref nextGroup, actions);
                        break;

                    case SubMenu subMenu:
                        var sub = menu.AddSubMenu(group, AView.GenerateViewId(), IMenu.None, subMenu.Title)!;
                        if (Icon(subMenu.Svg) is { } icon)
                            sub.SetIcon(icon);
                        Fill(sub, subMenu.Items, 0, ref nextGroup, actions);
                        break;

                    case MenuPicker picker:
                        var pickerGroup = nextGroup++;
                        foreach (var choice in picker.Items)
                            AddAction(menu, choice, picker, pickerGroup, actions);
                        menu.SetGroupCheckable(pickerGroup, true, true);
                        break;
                }
            }
        }

        void AddAction(IMenu menu, MenuAction action, MenuPicker? picker, int group, Dictionary<int, (MenuAction, MenuPicker?)> actions)
        {
            var id = AView.GenerateViewId();
            var item = menu.Add(group, id, IMenu.None, Title(action))!;

            item.SetEnabled(action.IsEnabled);

            if (picker is not null || action.KeepsMenuOpen)
                item.SetCheckable(true).SetChecked(action.IsChecked);

            if (Icon(action.Svg) is { } icon)
                item.SetIcon(icon);

            actions[id] = (action, picker);
        }

        static Java.Lang.ICharSequence Title(MenuAction action)
        {
            if (!action.IsDestructive)
                return new Java.Lang.String(action.Title);

            var text = new SpannableString(action.Title);
            text.SetSpan(new ForegroundColorSpan(Android.Graphics.Color.Argb(255, 211, 47, 47)), 0, action.Title.Length, SpanTypes.ExclusiveExclusive);
            return text;
        }

        Drawable? Icon(string? svg)
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            if (MenuButton.Icon(handler, svg, 24, dark ? Colors.White : Colors.Black) is not { } png)
                return null;

            var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(png, 0, png.Length);
            return bitmap is null ? null : new BitmapDrawable(handler.MauiContext?.Context?.Resources, bitmap);
        }
    }
}
