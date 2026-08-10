using AndroidX.Core.View;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

partial class SpineTabPage
{
    // Same padding discipline as SpineHostPage on Android: MAUI's ContentPageHandler re-applies
    // window-inset padding on every layout pass, which would break Spine's explicit edge-to-edge
    // management inside the tab host.
    private bool _paddingManaged;
    private bool _applyingManagedPadding;

    /// <summary>
    /// Attaches the <see cref="SystemInsetsProvider"/> to this tab page's native view for
    /// measurement and keeps zero padding asserted so content renders edge-to-edge behind the
    /// status bar. The bottom edge belongs to the native tab bar.
    /// </summary>
    internal void InitializeEdgeToEdgeInsets(SystemInsetsProvider insetsProvider)
    {
        _paddingManaged = true;
        ApplyZeroPadding();

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
            ViewCompat.SetOnApplyWindowInsetsListener(view, new TabPageInsetsConsumer());
        }
    }

    private void ApplyZeroPadding()
    {
        if (_applyingManagedPadding) return;
        _applyingManagedPadding = true;
        Padding = Thickness.Zero;
        _applyingManagedPadding = false;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (_paddingManaged && !_applyingManagedPadding && propertyName == nameof(Padding) && Padding != Thickness.Zero)
            ApplyZeroPadding();
    }

    // Zeroes native padding and consumes system-bar insets so MAUI's own listener cannot
    // re-apply top padding between HandlerChanged and Loaded.
    private sealed class TabPageInsetsConsumer : Java.Lang.Object, IOnApplyWindowInsetsListener
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
