using Android.Views;
using AndroidX.Core.View;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

partial class SpineHostPage
{
    // Tracks the padding Spine wants to apply so it can be re-asserted whenever
    // MAUI's ContentPageHandler re-applies window-inset-based padding (which would
    // otherwise override Spine's edge-to-edge management on every layout pass).
    private bool _paddingManaged;
    private bool _applyingManagedPadding;

    /// <summary>
    /// Attaches the <see cref="SystemInsetsProvider"/> as the insets listener on this page's
    /// native view and ensures zero padding is maintained. Must be called after the window is
    /// created so the platform view is available.
    /// </summary>
    internal void InitializeEdgeToEdgeInsets(SystemInsetsProvider insetsProvider)
    {
        // Enforce zero padding immediately so MAUI's default safe-area padding never shows.
        _paddingManaged = true;
        ApplyZeroPadding();

        // HandlerChanged fires after ConnectHandler completes, so MAUI may have already set
        // padding. Override immediately and on every handler reconnect.
        HandlerChanged += (_, _) => ApplyZeroPaddingAndConsumer();
        ApplyZeroPaddingAndConsumer();

        Loaded += (_, _) =>
        {
            if (Handler?.PlatformView is not Android.Views.View nativeView)
                return;

            insetsProvider.AttachTo(nativeView);
        };

        void ApplyZeroPaddingAndConsumer()
        {
            if (Handler?.PlatformView is not Android.Views.View view)
                return;

            view.SetPadding(0, 0, 0, 0);
            ViewCompat.SetOnApplyWindowInsetsListener(view, new SpineHostPageInsetsConsumer());
        }
    }

    private void ApplyZeroPadding()
    {
        if (_applyingManagedPadding) return;
        _applyingManagedPadding = true;
        Padding = Thickness.Zero;
        _applyingManagedPadding = false;
    }

    /// <summary>
    /// Re-asserts zero padding whenever MAUI's <see cref="ContentPage"/> handler
    /// re-applies window-inset-based padding (which happens on every layout pass in .NET MAUI 10
    /// on Android and would otherwise override edge-to-edge settings).
    /// </summary>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (_paddingManaged && !_applyingManagedPadding && propertyName == nameof(Padding) && Padding != Thickness.Zero)
            ApplyZeroPadding();
    }

    // Temporary insets consumer active between HandlerChanged and Loaded.
    // Zeroes native padding and consumes system-bar insets so MAUI's own listener
    // cannot re-apply top/bottom padding in that window.
    private sealed class SpineHostPageInsetsConsumer : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            v?.SetPadding(0, 0, 0, 0);

            if (insets is null)
                return insets;

            return new WindowInsetsCompat.Builder(insets)
                .SetInsets(WindowInsetsCompat.Type.SystemBars(), AndroidX.Core.Graphics.Insets.None)!
                .Build();
        }
    }
}
