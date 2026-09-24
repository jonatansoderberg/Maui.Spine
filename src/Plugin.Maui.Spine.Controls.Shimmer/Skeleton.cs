namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Turns a real layout into its own skeleton while its data loads: the views keep their places
/// but are hidden, and each is drawn as a rounded block with the shimmer wave over it. Labels
/// become bars, images blocks; containers (layouts, borders with content) keep drawing.
/// </summary>
/// <example>
/// <code language="xml"><![CDATA[
/// <VerticalStackLayout Skeleton.IsActive="{Binding IsLoading}">
///     <Label Text="{Binding Name}" FontSize="20" />
///     <Label Text="{Binding Bio}" Skeleton.Lines="3" />
///     <Image Source="{Binding Photo}" HeightRequest="160" />
/// </VerticalStackLayout>
/// ]]></code>
/// </example>
public static class Skeleton
{
    private const double FadeOutMilliseconds = 150;

    static Skeleton() => ShimmerStrings.EnsureRegistered();

    /// <summary>Shows the layout as a skeleton while true. Set on a <see cref="Layout"/>.</summary>
    public static readonly BindableProperty IsActiveProperty = BindableProperty.CreateAttached(
        "IsActive", typeof(bool), typeof(Skeleton), false, propertyChanged: OnIsActiveChanged);

    /// <summary>
    /// The number of bars a label is drawn as, and the lines of height it keeps while it has no
    /// text. 0 (the default) is one line, or as many as the label shows when it has text.
    /// </summary>
    public static readonly BindableProperty LinesProperty = BindableProperty.CreateAttached(
        "Lines", typeof(int), typeof(Skeleton), 0, propertyChanged: OnOverrideChanged);

    /// <summary>
    /// The width of a view's block (a label's last bar). From 0 to 1 a fraction of the width the view
    /// could take, above 1 device-independent units. Unset: a label with text keeps its text's width,
    /// one without text 60 %; other views their own width.
    /// </summary>
    public static readonly BindableProperty WidthProperty = BindableProperty.CreateAttached(
        "Width", typeof(double), typeof(Skeleton), -1d, propertyChanged: OnOverrideChanged);

    /// <summary>
    /// The height a view keeps while the layout is a skeleton, for a view that has no size without
    /// its data. Unset: a label keeps its lines' height, an image its requested size or a square.
    /// </summary>
    public static readonly BindableProperty HeightProperty = BindableProperty.CreateAttached(
        "Height", typeof(double), typeof(Skeleton), -1d, propertyChanged: OnOverrideChanged);

    /// <summary>The look of this skeleton layout; unset, the app-wide <c>DefaultShimmerStyleOptions</c> and the theme.</summary>
    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.CreateAttached(
        "StyleOptions", typeof(ShimmerStyleOptions), typeof(Skeleton), null,
        propertyChanged: (b, _, n) =>
        {
            if (GetOverlay(b) is { } overlay)
                overlay.StyleOptions = (ShimmerStyleOptions?)n;
        });

    /// <summary>
    /// Width of the band as a fraction of the skeleton layout's width, 0–1 (1 = as wide as the layout).
    /// Wins over <see cref="ShimmerStyleOptions.WaveWidth"/>. Set on the layout with <see cref="IsActiveProperty"/>.
    /// </summary>
    public static readonly BindableProperty WaveWidthProperty = BindableProperty.CreateAttached(
        "WaveWidth", typeof(double?), typeof(Skeleton), null,
        propertyChanged: (b, _, n) =>
        {
            if (GetOverlay(b) is { } overlay)
                overlay.WaveWidth = (double?)n;
        });

    /// <summary>
    /// Peak alpha at the centre of the band, 0–1. Wins over <see cref="ShimmerStyleOptions.WaveOpacity"/>.
    /// Set on the layout with <see cref="IsActiveProperty"/>.
    /// </summary>
    public static readonly BindableProperty WaveOpacityProperty = BindableProperty.CreateAttached(
        "WaveOpacity", typeof(double?), typeof(Skeleton), null,
        propertyChanged: (b, _, n) =>
        {
            if (GetOverlay(b) is { } overlay)
                overlay.WaveOpacity = (double?)n;
        });

    private static readonly BindableProperty OverlayProperty = BindableProperty.CreateAttached(
        "Overlay", typeof(SkeletonOverlay), typeof(Skeleton), null);

    public static bool GetIsActive(BindableObject view) => (bool)view.GetValue(IsActiveProperty);
    public static void SetIsActive(BindableObject view, bool value) => view.SetValue(IsActiveProperty, value);

    public static int GetLines(BindableObject view) => (int)view.GetValue(LinesProperty);
    public static void SetLines(BindableObject view, int value) => view.SetValue(LinesProperty, value);

    public static double GetWidth(BindableObject view) => (double)view.GetValue(WidthProperty);
    public static void SetWidth(BindableObject view, double value) => view.SetValue(WidthProperty, value);

    public static double GetHeight(BindableObject view) => (double)view.GetValue(HeightProperty);
    public static void SetHeight(BindableObject view, double value) => view.SetValue(HeightProperty, value);

    public static ShimmerStyleOptions? GetStyleOptions(BindableObject view) => (ShimmerStyleOptions?)view.GetValue(StyleOptionsProperty);
    public static void SetStyleOptions(BindableObject view, ShimmerStyleOptions? value) => view.SetValue(StyleOptionsProperty, value);

    public static double? GetWaveWidth(BindableObject view) => (double?)view.GetValue(WaveWidthProperty);
    public static void SetWaveWidth(BindableObject view, double? value) => view.SetValue(WaveWidthProperty, value);

    public static double? GetWaveOpacity(BindableObject view) => (double?)view.GetValue(WaveOpacityProperty);
    public static void SetWaveOpacity(BindableObject view, double? value) => view.SetValue(WaveOpacityProperty, value);

    private static SkeletonOverlay? GetOverlay(BindableObject view) => (SkeletonOverlay?)view.GetValue(OverlayProperty);

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Layout layout)
        {
            throw new InvalidOperationException(
                $"Skeleton.IsActive works on a layout (a StackLayout, Grid, FlexLayout, …); it was set on a {bindable.GetType().Name}. Put it on the layout inside.");
        }

        if ((bool)newValue)
            Activate(layout);
        else
            Deactivate(layout);
    }

    private static void Activate(Layout layout)
    {
        if (GetOverlay(layout) is not null)
            return;

        var overlay = new SkeletonOverlay(layout, skeletonRoot: null)
        {
            StyleOptions = GetStyleOptions(layout),
            WaveWidth = GetWaveWidth(layout),
            WaveOpacity = GetWaveOpacity(layout),
        };
        layout.SetValue(OverlayProperty, overlay);
        layout.ChildRemoved += OnChildRemoved;
        layout.Children.Add(overlay);
    }

    private static void Deactivate(Layout layout)
    {
        if (GetOverlay(layout) is not { } overlay)
            return;

        layout.ClearValue(OverlayProperty);
        layout.ChildRemoved -= OnChildRemoved;

        // The views come back at once, in the places they held; the blocks fade over them.
        overlay.Release();

        if (ReduceMotion.IsEnabled || overlay.Handler is null)
        {
            layout.Children.Remove(overlay);
            return;
        }

        overlay.FadeToAsync(0, (uint)FadeOutMilliseconds).ContinueWith(
            _ => overlay.Dispatcher.Dispatch(() => layout.Children.Remove(overlay)),
            TaskScheduler.Default);
    }

    // BindableLayout clears the children when its source is reset; the overlay goes back on top.
    private static void OnChildRemoved(object? sender, ElementEventArgs e)
    {
        if (sender is Layout layout && GetOverlay(layout) is { } overlay && e.Element == overlay)
        {
            layout.Dispatcher.Dispatch(() =>
            {
                if (GetOverlay(layout) == overlay && !layout.Children.Contains(overlay))
                    layout.Children.Add(overlay);

                layout.Dispatcher.Dispatch(overlay.EnsureAttached);
            });
        }
    }

    private static void OnOverrideChanged(BindableObject bindable, object oldValue, object newValue)
    {
        for (var element = (bindable as Element)?.Parent; element is not null; element = element.Parent)
        {
            if (GetOverlay(element) is { } overlay)
            {
                overlay.QueueRefresh();
                return;
            }
        }
    }
}
