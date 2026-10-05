using Android.Views;
using AndroidX.Core.View;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Android implementation of <see cref="ISystemInsetsProvider"/>.
/// Measures system bar heights via <c>WindowInsetsCompat</c> and exposes them in
/// device-independent pixels. Automatically wired up by Spine's DI registration.
/// </summary>
internal sealed class SystemInsetsProvider : Java.Lang.Object, ISystemInsetsProvider, IOnApplyWindowInsetsListener
{
    private Thickness _systemBarInsets;
    private bool _hasMeasured;
    private static bool _imeAnimating;

    /// <inheritdoc/>
    public Thickness SystemBarInsets => _systemBarInsets;

    /// <inheritdoc/>
    public event Action? InsetsChanged;

    /// <summary>
    /// Synchronously measures system bar insets so that <see cref="SystemBarInsets"/> is
    /// immediately available to ViewModels — even in their constructors.
    /// Tries <see cref="ViewCompat.GetRootWindowInsets"/> first (accurate when insets have
    /// already been dispatched). Falls back to reading <c>status_bar_height</c> and
    /// <c>navigation_bar_height</c> from Android system resources, which are always available
    /// once the Activity exists. The <see cref="OnApplyWindowInsets"/> listener will refine
    /// the values later if they differ (e.g. due to display cutouts).
    /// </summary>
    internal void MeasureInitialInsets()
    {
        if (_hasMeasured)
            return;

        if (Platform.CurrentActivity?.Window?.DecorView is not { } decorView)
            return;

        var resources = decorView.Resources;
        if (resources is null)
            return;

        var density = (double)(resources.DisplayMetrics?.Density ?? 1f);

        // Prefer the WindowInsetsCompat path — it's accurate and includes display cutouts.
        var rootInsets = ViewCompat.GetRootWindowInsets(decorView);
        if (rootInsets is not null)
        {
            var bars = rootInsets.GetInsets(WindowInsetsCompat.Type.SystemBars())!;

            _systemBarInsets = new Thickness(
                bars.Left / density,
                bars.Top / density,
                bars.Right / density,
                bars.Bottom / density);

            _hasMeasured = true;
            InsetsChanged?.Invoke();
            return;
        }

        // Fallback: read system bar dimensions from Android resources.
        // Available synchronously on all API levels once the Activity exists.
        var statusBarId = resources.GetIdentifier("status_bar_height", "dimen", "android");
        var navBarId = resources.GetIdentifier("navigation_bar_height", "dimen", "android");

        var top = statusBarId > 0 ? resources.GetDimensionPixelSize(statusBarId) / density : 0;
        var bottom = navBarId > 0 ? resources.GetDimensionPixelSize(navBarId) / density : 0;

        _systemBarInsets = new Thickness(0, top, 0, bottom);
        _hasMeasured = true;
        InsetsChanged?.Invoke();
    }

    /// <summary>
    /// Attaches this provider as the <see cref="IOnApplyWindowInsetsListener"/> on the given
    /// native view so it can capture system bar measurements. Must be called after the window
    /// is created and the platform view is available.
    /// </summary>
    internal void AttachTo(Android.Views.View nativeView)
    {
        ViewCompat.SetOnApplyWindowInsetsListener(nativeView, this);
        FollowKeyboard(nativeView.RootView ?? nativeView);
        ViewCompat.RequestApplyInsets(nativeView);
    }

    /// <summary>
    /// Callback from the Android insets system. Measures system bar heights on first call
    /// (and on configuration changes that alter insets), zeroes native padding, and consumes
    /// system bar insets so MAUI does not re-apply its own safe-area padding.
    /// </summary>
    public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
    {
        if (v is null || insets is null)
            return insets;

        // Zero any native padding that MAUI's ContentPageHandler may have set.
        v.SetPadding(0, 0, 0, 0);

        var density = (double)(v.Resources?.DisplayMetrics?.Density ?? 1f);
        var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars())!;

        var newInsets = new Thickness(
            bars.Left / density,
            bars.Top / density,
            bars.Right / density,
            bars.Bottom / density);

        if (!_hasMeasured || newInsets != _systemBarInsets)
        {
            _systemBarInsets = newInsets;
            _hasMeasured = true;
            InsetsChanged?.Invoke();
        }

        ReportKeyboard(v, insets);

        // Consume system bar insets so MAUI's own listener cannot re-apply padding, and the
        // keyboard's, which NavigationRegion answers for the page (SoftKeyboard).
        return new WindowInsetsCompat.Builder(insets)
            .SetInsets(WindowInsetsCompat.Type.SystemBars(), AndroidX.Core.Graphics.Insets.None)!
            .SetInsets(WindowInsetsCompat.Type.Ime(), AndroidX.Core.Graphics.Insets.None)!
            .Build();
    }

    /// <summary>
    /// Reports where the keyboard ends up, from the insets <paramref name="view"/> in the activity's
    /// or a sheet's window was given. Only the window with the focus owns the keyboard: the activity
    /// gets the keyboard's insets too while a sheet's field has it, ahead of the sheet's animation.
    /// While the keyboard moves, those insets are already its end state; the animation reports the frames.
    /// </summary>
    internal static void ReportKeyboard(Android.Views.View view, WindowInsetsCompat insets)
    {
        if (!_imeAnimating && view.HasWindowFocus)
            ReportKeyboardAt(view.RootView ?? view, insets);
    }

    /// <summary>
    /// Reports the keyboard's top edge on screen. Its inset is measured up from the bottom of the
    /// window, which <paramref name="root"/> fills edge to edge.
    /// </summary>
    private static void ReportKeyboardAt(Android.Views.View root, WindowInsetsCompat insets)
    {
        var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime())!.Bottom;
        if (ime <= 0)
        {
            SoftKeyboard.Report(null, TimeSpan.Zero);
            return;
        }

        var density = (double)(root.Resources?.DisplayMetrics?.Density ?? 1f);
        var location = new int[2];
        root.GetLocationOnScreen(location);
        SoftKeyboard.Report((location[1] + root.Height - ime) / density, TimeSpan.Zero);
    }

    /// <summary>
    /// Reports the keyboard frame by frame while it moves over the window that <paramref name="root"/>
    /// is the root of: the activity's, or a sheet's dialog. On the root, above the view MAUI gives
    /// its own callback: MAUI's stops the dispatch, so a callback below it never hears of the move.
    /// </summary>
    internal static void FollowKeyboard(Android.Views.View root) =>
        ViewCompat.SetWindowInsetsAnimationCallback(root, new ImeAnimationCallback(root));

    /// <summary>
    /// Follows the keyboard frame by frame while it slides in or out, so the page's padding moves
    /// with it as it does in a native app, instead of jumping to the end at the start.
    /// </summary>
    private sealed class ImeAnimationCallback(Android.Views.View root)
        : WindowInsetsAnimationCompat.Callback(DispatchModeContinueOnSubtree)
    {
        public override void OnPrepare(WindowInsetsAnimationCompat? animation)
        {
            if (animation is not null && IsIme(animation))
                _imeAnimating = true;
        }

        public override WindowInsetsCompat OnProgress(WindowInsetsCompat? insets, IList<WindowInsetsAnimationCompat>? runningAnimations)
        {
            if (insets is not null && runningAnimations?.Any(IsIme) == true)
                ReportKeyboardAt(root, insets);

            return insets!;
        }

        public override void OnEnd(WindowInsetsAnimationCompat? animation)
        {
            if (animation is null || !IsIme(animation))
                return;

            _imeAnimating = false;

            if (ViewCompat.GetRootWindowInsets(root) is { } insets)
                ReportKeyboardAt(root, insets);
        }

        private static bool IsIme(WindowInsetsAnimationCompat animation) =>
            (animation.TypeMask & WindowInsetsCompat.Type.Ime()) != 0;
    }
}
