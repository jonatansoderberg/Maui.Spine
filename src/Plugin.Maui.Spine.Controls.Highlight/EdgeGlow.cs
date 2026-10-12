using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Edge round the whole window: a view the window's size, shown with <see cref="SpineOverlay"/> so it lies
/// above the header bar, the tab bar and sheets and takes no touches, carrying the per-view Edge. Its rim lies
/// just inside the screen's edge, rounded like the display, and the outer half of its glow falls off screen.
/// </summary>
internal sealed class EdgeGlow : IEdgeGlow
{
    // Room for a glow three times Edge's usual on a view: the outer half falls off screen.
    const double GlowRoom = 36;

    readonly Grid _view;
    readonly Border _surface;
    readonly IDisposable _overlay;
    readonly EdgeLevel _level = new();
    FrameClock? _clock;
    Func<float>? _intensity;
    bool _disposed;

    public EdgeGlow(Window window)
    {
        _surface = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Microsoft.Maui.Graphics.Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = DisplayRadius(window) },
            InputTransparent = true,
        };
        Highlight.SetOutset(_surface, GlowRoom);
        Highlight.SetRepeat(_surface, HighlightRepeat.Forever);
        Highlight.SetKind(_surface, HighlightKind.Edge);

        _view = new Grid { InputTransparent = true, SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None, Children = { _surface } };
        _overlay = SpineOverlay.Show(_view, passTouches: true);
    }

    public Func<float>? Intensity
    {
        get => _intensity;
        set
        {
            _intensity = value;
            _clock?.Dispose();
            _clock = value is null || _disposed ? null : new FrameClock(Tick);
            if (value is null)
                Highlight.GetState(_surface)?.Refresh();
        }
    }

    public IReadOnlyList<Color>? Colors
    {
        get => Highlight.GetColors(_surface);
        set => Highlight.SetColors(_surface, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _clock?.Dispose();
        _clock = null;
        FadeOutAsync().SafeFireAndForget();
    }

    async Task FadeOutAsync()
    {
        await _view.FadeToAsync(0, ReducedMotion.IsOn ? 0u : 250u);
        Highlight.SetKind(_surface, HighlightKind.None);
        _overlay.Dispose();
    }

    void Tick()
    {
        if (_intensity is not { } intensity)
            return;

        float target;
        try
        {
            target = intensity();
        }
        catch (Exception exception)
        {
            // The caller's function failed: the glow stops rather than showing a level nobody asked for.
            System.Diagnostics.Debug.WriteLine($"[Spine] IEdgeGlow.Intensity threw, so the window's edge glow stopped: {exception}");
            Dispose();
            return;
        }

        Highlight.GetState(_surface)?.SetLevel(_level.Step(target));
    }

    /// <summary>The display's corner radius in points, which the rim follows.</summary>
    static double DisplayRadius(Window window)
    {
#if IOS
        if (window.Handler?.PlatformView is UIKit.UIWindow platformWindow)
        {
            var bounds = UIKit.UIScreen.MainScreen.Bounds;
            return DisplayCorners.Radius(bounds.Width, bounds.Height,
                hasHomeIndicator: platformWindow.SafeAreaInsets.Bottom > 0,
                isPad: UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Pad);
        }
#elif ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && Platform.CurrentActivity?.Window?.DecorView?.RootWindowInsets?.GetRoundedCorner((int)Android.Views.RoundedCornerPosition.TopLeft) is { } corner)
        {
            return corner.Radius / (Platform.CurrentActivity.Resources?.DisplayMetrics?.Density ?? 1);
        }
#endif
        return 0;
    }
}
