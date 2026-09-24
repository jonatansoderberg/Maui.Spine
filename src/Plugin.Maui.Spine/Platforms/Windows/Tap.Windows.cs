using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WColor = Windows.UI.Color;
using WControl = Microsoft.UI.Xaml.Controls.Control;
using WDependencyObject = Microsoft.UI.Xaml.DependencyObject;
using WVisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class TapState
{
    FrameworkElement? _host;
    SpriteVisual? _overlay;
    CompositionRoundedRectangleGeometry? _clip;
    bool _pressed;
    bool _hovered;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not FrameworkElement host)
            return;

        _host = host;
        host.Tapped += OnTapped;
        host.PointerEntered += OnPointerEntered;
        host.PointerExited += OnPointerExited;
        host.PointerPressed += OnPointerPressed;
        host.PointerReleased += OnPointerReleased;
        host.PointerCanceled += OnPointerExited;
        host.PointerCaptureLost += OnPointerExited;
        host.SizeChanged += OnSizeChanged;
    }

    partial void DisconnectPlatform()
    {
        if (_host is not { } host)
            return;

        host.Tapped -= OnTapped;
        host.PointerEntered -= OnPointerEntered;
        host.PointerExited -= OnPointerExited;
        host.PointerPressed -= OnPointerPressed;
        host.PointerReleased -= OnPointerReleased;
        host.PointerCanceled -= OnPointerExited;
        host.PointerCaptureLost -= OnPointerExited;
        host.SizeChanged -= OnSizeChanged;

        if (_overlay is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(host, null);
            _overlay.Dispose();
        }

        _overlay = null;
        _clip = null;
        _host = null;
        _pressed = _hovered = false;
    }

    partial void UpdatePlatform()
    {
        if (!CanExecute)
        {
            _pressed = _hovered = false;
            Paint();
        }
    }

    // A control in the view (a toggle switch in a row) handles its own taps.
    bool FromOwnControl(object? source)
    {
        for (var element = source as WDependencyObject; element is not null && element != _host; element = WVisualTreeHelper.GetParent(element))
        {
            if (element is WControl)
                return true;
        }

        return false;
    }

    void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.Handled || FromOwnControl(e.OriginalSource))
            return;

        e.Handled = true;
        Execute();
    }

    void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = CanExecute;
        Paint();
    }

    void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = _pressed = false;
        Paint();
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressed = CanExecute && !FromOwnControl(e.OriginalSource);
        Paint();
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        Paint();
    }

    void OnSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        if (_clip is not null)
            _clip.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
    }

    void Paint()
    {
        if (_host is not { } host)
            return;

        if (_overlay is null)
        {
            if (!_pressed && !_hovered)
                return;

            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            _overlay = compositor.CreateSpriteVisual();
            _overlay.RelativeSizeAdjustment = Vector2.One;

            var radius = (float)CornerRadius();
            if (radius > 0)
            {
                _clip = compositor.CreateRoundedRectangleGeometry();
                _clip.CornerRadius = new Vector2(radius, radius);
                _clip.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                _overlay.Clip = compositor.CreateGeometricClip(_clip);
            }

            // The child visual draws above the element's own content.
            ElementCompositionPreview.SetElementChildVisual(host, _overlay);
        }

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var custom = HighlightColor;
        WColor color = custom is not null
            ? WColor.FromArgb((byte)(custom.Alpha * 255), (byte)(custom.Red * 255), (byte)(custom.Green * 255), (byte)(custom.Blue * 255))
            : dark ? WColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : WColor.FromArgb(0x14, 0x00, 0x00, 0x00);

        _overlay.Brush = _overlay.Compositor.CreateColorBrush(color);
        _overlay.Opacity = _pressed ? 1f : _hovered ? 0.5f : 0f;
    }
}
