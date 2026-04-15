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
            var bars = rootInsets.GetInsets(WindowInsetsCompat.Type.SystemBars()) ?? AndroidX.Core.Graphics.Insets.None;

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
        var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()) ?? AndroidX.Core.Graphics.Insets.None;

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

        // Consume system bar insets so MAUI's own listener cannot re-apply padding.
        return new WindowInsetsCompat.Builder(insets)
            .SetInsets(WindowInsetsCompat.Type.SystemBars(), AndroidX.Core.Graphics.Insets.None)!
            .Build();
    }
}
