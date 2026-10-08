#if ANDROID
using Android.Content;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using AndroidX.Core.Widget;
using Google.Android.Material.BottomSheet;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Presentation;

/// <remarks>
/// A Material 3 modal bottom sheet with a list: the drag handle, the title and message, and one
/// 56 dp row per action. It has no Cancel row; Material sheets go with a swipe, the scrim or Back.
/// </remarks>
internal static partial class ActionSheetPresenter
{
    private static partial Task<MenuAction?> ShowPlatformAsync(IServiceProvider services, ActionSheet sheet, IReadOnlyList<MenuAction> actions, Microsoft.Maui.Controls.View? anchor)
    {
        if (Platform.CurrentActivity is not { } activity)
            throw new InvalidOperationException("ShowActionsAsync found no activity to show the action sheet in.");

        var done = new TaskCompletionSource<MenuAction?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new BottomSheetDialog(activity);
        var context = dialog.Context;

        var onSurface = BottomSheetPageExtensions.ResolveMaterialColor(context, "colorOnSurface", AColor.Black);
        var onSurfaceVariant = BottomSheetPageExtensions.ResolveMaterialColor(context, "colorOnSurfaceVariant", AColor.DarkGray);
        var error = BottomSheetPageExtensions.ResolveMaterialColor(context, "colorError", AColor.Rgb(179, 38, 30));

        var list = new LinearLayout(context) { Orientation = Orientation.Vertical };
        list.SetPadding(0, 0, 0, Dp(context, 8));
        list.AddView(new BottomSheetDragHandleView(context));

        if (!string.IsNullOrEmpty(sheet.Title) || !string.IsNullOrEmpty(sheet.Message))
            list.AddView(Header(context, sheet, onSurface, onSurfaceVariant));

        foreach (var action in actions)
        {
            var color = action.IsDestructive ? error : onSurface;
            var iconColor = action.IsDestructive ? error : onSurfaceVariant;
            list.AddView(Row(context, services, action, color, iconColor, () =>
            {
                done.TrySetResult(action);
                dialog.Dismiss();
            }));
        }

        var scroll = new NestedScrollView(context);
        scroll.AddView(list);
        dialog.SetContentView(scroll);

        // TalkBack reads the window's title when the sheet appears.
        if (!string.IsNullOrEmpty(sheet.Title))
            dialog.SetTitle(sheet.Title);

        dialog.Behavior.SkipCollapsed = true;
        dialog.Behavior.State = BottomSheetBehavior.StateExpanded;
        dialog.DismissEvent += (_, _) => done.TrySetResult(null);
        dialog.Show();

        return done.Task;
    }

    static LinearLayout Header(Context context, ActionSheet sheet, AColor titleColor, AColor messageColor)
    {
        var header = new LinearLayout(context) { Orientation = Orientation.Vertical };
        header.SetPadding(Dp(context, 24), 0, Dp(context, 24), Dp(context, 12));

        if (!string.IsNullOrEmpty(sheet.Title))
        {
            var title = new TextView(context) { Text = sheet.Title };
            title.SetTextSize(ComplexUnitType.Sp, 16);
            title.SetTextColor(titleColor);
            title.SetTypeface(Android.Graphics.Typeface.Create("sans-serif-medium", Android.Graphics.TypefaceStyle.Normal), Android.Graphics.TypefaceStyle.Normal);
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                title.AccessibilityHeading = true;
            header.AddView(title);
        }

        if (!string.IsNullOrEmpty(sheet.Message))
        {
            var message = new TextView(context) { Text = sheet.Message };
            message.SetTextSize(ComplexUnitType.Sp, 14);
            message.SetTextColor(messageColor);
            message.SetPadding(0, Dp(context, 4), 0, 0);
            header.AddView(message);
        }

        return header;
    }

    static LinearLayout Row(Context context, IServiceProvider services, MenuAction action, AColor color, AColor iconColor, System.Action picked)
    {
        var row = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            Clickable = true,
            Focusable = true,
            Enabled = action.IsEnabled,
        };
        row.SetAccessibilityDelegate(new ButtonRole());
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(Dp(context, 56));
        row.SetPadding(Dp(context, 24), 0, Dp(context, 24), 0);
        row.Background = SelectableBackground(context);
        row.Click += (_, _) => picked();

        if (MenuButton.Icon(services, action.Svg, 24, Color.FromUint((uint)iconColor.ToArgb())) is { } png
            && Android.Graphics.BitmapFactory.DecodeByteArray(png, 0, png.Length) is { } bitmap)
        {
            var icon = new ImageView(context) { ImportantForAccessibility = ImportantForAccessibility.No };
            icon.SetImageDrawable(new BitmapDrawable(context.Resources, bitmap));
            var size = Dp(context, 24);
            row.AddView(icon, new LinearLayout.LayoutParams(size, size) { MarginEnd = Dp(context, 16) });
        }

        var title = new TextView(context) { Text = action.Title };
        title.SetTextSize(ComplexUnitType.Sp, 16);
        title.SetTextColor(color);
        title.SetPadding(0, Dp(context, 8), 0, Dp(context, 8));
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));

        if (!action.IsEnabled)
            row.Alpha = 0.38f;

        return row;
    }

    static Drawable? SelectableBackground(Context context)
    {
        var value = new TypedValue();
        return context.Theme?.ResolveAttribute(Android.Resource.Attribute.SelectableItemBackground, value, true) == true
            ? context.GetDrawable(value.ResourceId)
            : null;
    }

    static int Dp(Context context, double dp) => (int)Math.Round(dp * (context.Resources?.DisplayMetrics?.Density ?? 1));

    /// <summary>A row is a button to TalkBack: "Share, button, double-tap to activate".</summary>
    sealed class ButtonRole : AView.AccessibilityDelegate
    {
        public override void OnInitializeAccessibilityNodeInfo(AView host, AccessibilityNodeInfo info)
        {
            base.OnInitializeAccessibilityNodeInfo(host, info);
            info.ClassName = "android.widget.Button";
        }
    }
}
#endif
