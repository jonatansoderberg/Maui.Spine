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
    /// Keeps <paramref name="window"/>, a sheet that opened while an overlay shows, out of sight until the
    /// overlays have gone. A later window goes on top, and showing the overlay again to get back above it
    /// would leave a frame or two with neither.
    /// </summary>
    internal static void HoldUnder(Android.Views.Window? window)
    {
        if (_shown.Count == 0 || window?.DecorView is not { } decor)
            return;

        decor.Visibility = ViewStates.Invisible;
        _heldUnder.Add(window);
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

            var view = _content.ToPlatform(context);
            (view.Parent as ViewGroup)?.RemoveView(view);

            var dialog = new OverlayDialog(activity, _back);
            dialog.SetContentView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            if (dialog.Window is { } platformWindow)
            {
                platformWindow.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
                platformWindow.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
                platformWindow.ClearFlags(WindowManagerFlags.DimBehind);
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
            _dialog = dialog;
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
