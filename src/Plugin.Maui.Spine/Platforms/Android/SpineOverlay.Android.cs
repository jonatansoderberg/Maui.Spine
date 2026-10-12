using Android.App;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineOverlay
{
    private static readonly List<Android.Views.Window> _heldUnder = [];

    /// <summary>The overlays' windows, which <see cref="SpineScreen.ExcludeFromCapture"/> marks secure too.</summary>
    internal static IEnumerable<Android.Views.Window> Windows() =>
        _shown.Select(handle => handle.PlatformWindow).OfType<Android.Views.Window>();

    /// <summary>
    /// A sheet, <paramref name="window"/>, opened while overlays show; a later window goes on top. While an
    /// overlay that takes touches shows, the sheet is kept out of sight until the overlays have gone: showing
    /// the overlay again to get back above it would leave a frame or two with neither. Overlays that pass
    /// touches through (a glow) are shown again above the sheet instead; a frame without them does no harm.
    /// </summary>
    internal static void OnSheetShown(Android.Views.Window? window)
    {
        if (window?.DecorView is not { } decor)
            return;

        if (_shown.Any(handle => !handle.PassTouches))
        {
            decor.Visibility = ViewStates.Invisible;
            _heldUnder.Add(window);
            return;
        }

        foreach (var handle in _shown)
            handle.Raise();
    }

    private static void ReleaseHeld()
    {
        foreach (var window in _heldUnder)
            window.DecorView.Visibility = ViewStates.Visible;

        _heldUnder.Clear();
    }

    private sealed partial class Handle
    {
        private OverlayDialog? _dialog;

        public Android.Views.Window? PlatformWindow => _dialog?.Window;

        /// <remarks>
        /// A full-screen dialog of its own, as <c>LightboxOverlay</c> uses: a sheet is a dialog window, and a
        /// view in the activity's window would be under it.
        /// </remarks>
        public partial void Attach(IMauiContext context)
        {
            if (Platform.CurrentActivity is not { } activity)
                return;

            _dialog = Open(activity, _content.ToPlatform(context));
        }

        /// <summary>
        /// Puts the overlay back on top of a later window: a window shown again keeps its place, so a new dialog
        /// takes the view, then the old one closes.
        /// </summary>
        public void Raise()
        {
            if (_dialog is not { IsShowing: true } old || Platform.CurrentActivity is not { } activity
                || _content.Handler?.PlatformView is not Android.Views.View view)
            {
                return;
            }

            _dialog = Open(activity, view);
            old.Dismiss();
        }

        OverlayDialog Open(Activity activity, Android.Views.View view)
        {
            (view.Parent as ViewGroup)?.RemoveView(view);

            var dialog = new OverlayDialog(activity, _back);
            dialog.SetContentView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            if (dialog.Window is { } platformWindow)
            {
                if (PassTouches)
                    platformWindow.AddFlags(WindowManagerFlags.NotTouchable | WindowManagerFlags.NotFocusable);

                platformWindow.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
                platformWindow.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
                platformWindow.ClearFlags(WindowManagerFlags.DimBehind);

                // Without it the system paints black bars behind the status and navigation bars, over the
                // app, instead of letting the overlay draw there.
                platformWindow.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
                WindowCompat.SetDecorFitsSystemWindows(platformWindow, false);
                if (!OperatingSystem.IsAndroidVersionAtLeast(35))
                {
                    platformWindow.SetStatusBarColor(Android.Graphics.Color.Transparent);
                    platformWindow.SetNavigationBarColor(Android.Graphics.Color.Transparent);
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(28) && platformWindow.Attributes is { } attributes)
                {
                    attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                    platformWindow.Attributes = attributes;
                }

                SpineScreen.Secure(platformWindow);
            }

            dialog.Show();
            return dialog;
        }

        private partial void Detach()
        {
            _dialog?.Dismiss();
            _dialog = null;

            if (_shown.Count == 0)
                ReleaseHeld();
        }
    }

    private sealed class OverlayDialog(Activity activity, Action? back) : Dialog(activity, Android.Resource.Style.ThemeTranslucentNoTitleBar)
    {
        public override void OnBackPressed() => back?.Invoke();
    }
}
