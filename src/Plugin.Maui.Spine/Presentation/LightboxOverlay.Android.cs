#if ANDROID
using Android.App;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class LightboxOverlay
{
    private OverlayDialog? _dialog;

    /// <summary>The overlay's window, whose status bar the lightbox hides and colours while it shows.</summary>
    public static Android.Views.Window? CurrentWindow => Current?._dialog?.Window;

    /// <remarks>
    /// A dialog of its own, full screen and see-through: the sheet is a dialog window too, and a view
    /// in the activity's window would be under it.
    /// </remarks>
    partial void Attach(IMauiContext context)
    {
        if (Platform.CurrentActivity is not { } activity)
            return;

        var view = _region.ToPlatform(context);
        (view.Parent as ViewGroup)?.RemoveView(view);

        var dialog = new OverlayDialog(activity, () => ViewModel.BackAsync());
        dialog.SetContentView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        if (dialog.Window is { } window)
        {
            window.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
            window.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            window.ClearFlags(WindowManagerFlags.DimBehind);
            WindowCompat.SetDecorFitsSystemWindows(window, false);
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);

            if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes)
            {
                attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                window.Attributes = attributes;
            }

            // White icons on the black.
            if (window.DecorView is { } decor)
                WindowCompat.GetInsetsController(window, decor)?.AppearanceLightStatusBars = false;
        }

        dialog.Show();
        _dialog = dialog;
    }

    partial void Detach()
    {
        _dialog?.Dismiss();
        _dialog = null;
    }

    private sealed class OverlayDialog(Activity activity, Func<Task> back) : Dialog(activity, Android.Resource.Style.ThemeTranslucentNoTitleBar)
    {
        // The system back closes the lightbox as its close button does.
        public override void OnBackPressed() => _ = back();
    }
}
#endif
