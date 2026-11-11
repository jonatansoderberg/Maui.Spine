using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using Point = Windows.Foundation.Point;
using Rect = Windows.Foundation.Rect;
using UIElement = Microsoft.UI.Xaml.UIElement;
using Visibility = Microsoft.UI.Xaml.Visibility;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The window's passthrough regions. With the content extended into the title bar, the title bar's
/// band and any caption region a control adds (a hero header) drag the window, and only passthrough
/// regions there take clicks. MAUI's <see cref="TitleBar"/> makes passthrough regions for its own
/// content alone, so the header bar's buttons under a hidden title bar, and the buttons over a hero
/// header, could not be clicked. Spine keeps the passthrough regions itself: every control that is on
/// top somewhere in a caption area, re-read after each layout pass.
/// </summary>
public partial class SpineApplication<TNavigable>
{
    private InputNonClientPointerSource? _nonClient;
    private FrameworkElement? _nonClientRoot;
    private bool _nonClientUpdatePending;
    private string? _nonClientLastError;

    private void HookNonClientRegions(WinUIWindow window, WindowId windowId)
    {
        UnhookNonClientRegions();

        if (window.Content is not FrameworkElement root)
        {
            void Retry(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
            {
                window.Activated -= Retry;
                HookNonClientRegions(window, windowId);
            }

            window.Activated += Retry;
            return;
        }

        _nonClient = InputNonClientPointerSource.GetForWindowId(windowId);
        _nonClientRoot = root;
        root.LayoutUpdated += OnNonClientLayoutUpdated;
        ScheduleNonClientUpdate();
    }

    private void UnhookNonClientRegions()
    {
        if (_nonClientRoot is not null)
            _nonClientRoot.LayoutUpdated -= OnNonClientLayoutUpdated;

        _nonClientRoot = null;
        _nonClient = null;
    }

    private void OnNonClientLayoutUpdated(object? sender, object e) => ScheduleNonClientUpdate();

    // Once per pass of the dispatcher, after the layout that asked for it.
    private void ScheduleNonClientUpdate()
    {
        if (_nonClientUpdatePending || _nonClientRoot is null)
            return;

        _nonClientUpdatePending = true;
        if (!_nonClientRoot.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdateNonClientRegions))
            _nonClientUpdatePending = false;
    }

    private void UpdateNonClientRegions()
    {
        _nonClientUpdatePending = false;

        if (_nonClient is not { } source || _nonClientRoot is not { XamlRoot: { } xamlRoot } root || _appWindow is null)
            return;

        try
        {
            var scale = xamlRoot.RasterizationScale;
            var band = new Rect(0, 0, root.ActualWidth, _appWindow.TitleBar.Height / scale);

            // A control that gave its caption region back (a hero header leaving) leaves none; the
            // title bar's band drags the window again until MAUI's title bar sets it on its next layout.
            var captions = source.GetRegionRects(NonClientRegionKind.Caption) ?? [];
            if (captions.Length == 0 && band.Height > 0)
            {
                captions = [ToPhysical(band, scale)];
                source.SetRegionRects(NonClientRegionKind.Caption, captions);
            }

            var window = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var passthrough = new List<RectInt32>();
            foreach (var area in captions.Select(c => ToDips(c, scale)).Append(band))
            {
                foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(area, root))
                {
                    if (!IsClickable(element) || !IsOnTop(element, root, out var bounds))
                        continue;

                    // Only the part in the region and the window: a button half scrolled away has
                    // bounds outside both.
                    bounds.Intersect(area);
                    bounds.Intersect(window);
                    if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
                        continue;

                    var rect = ToPhysical(bounds, scale);
                    if (!passthrough.Contains(rect))
                        passthrough.Add(rect);
                }
            }

            passthrough.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

            // Null rather than empty while the window is full screen, as the captions above may be.
            if (!(source.GetRegionRects(NonClientRegionKind.Passthrough) ?? []).SequenceEqual(passthrough))
                source.SetRegionRects(NonClientRegionKind.Passthrough, [.. passthrough]);
        }
        catch (Exception e)
        {
            // Run from the dispatcher, an exception here would end the app. The regions stay as they
            // were until the next layout pass.
            if (e.Message != _nonClientLastError)
            {
                _nonClientLastError = e.Message;
                _services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Windows")
                    .LogWarning(e, "Spine could not update the window's passthrough regions.");
            }
        }
    }

    private static bool IsClickable(UIElement element) =>
        element is ButtonBase or TextBox or PasswordBox or AutoSuggestBox or ComboBox or Microsoft.UI.Xaml.Controls.Slider or ToggleSwitch or RichEditBox
        && element is Microsoft.UI.Xaml.Controls.Control { IsEnabled: true, Visibility: Visibility.Visible, IsHitTestVisible: true };

    // Hit-testing a rectangle finds everything under it, also a button a hero photo covers; a button
    // takes clicks only where it is the topmost element.
    private static bool IsOnTop(UIElement element, UIElement root, out Rect bounds)
    {
        bounds = default;
        if (element is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } framework)
            return false;

        bounds = framework.TransformToVisual(root).TransformBounds(new Rect(0, 0, framework.ActualWidth, framework.ActualHeight));
        var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        for (var hit = VisualTreeHelper.FindElementsInHostCoordinates(centre, root).FirstOrDefault() as Microsoft.UI.Xaml.DependencyObject;
             hit is not null;
             hit = VisualTreeHelper.GetParent(hit))
        {
            if (ReferenceEquals(hit, element))
                return true;
        }

        return false;
    }

    private static RectInt32 ToPhysical(Rect rect, double scale) =>
        new((int)Math.Round(rect.X * scale), (int)Math.Round(rect.Y * scale), (int)Math.Round(rect.Width * scale), (int)Math.Round(rect.Height * scale));

    private static Rect ToDips(RectInt32 rect, double scale) =>
        new(rect.X / scale, rect.Y / scale, rect.Width / scale, rect.Height / scale);
}
