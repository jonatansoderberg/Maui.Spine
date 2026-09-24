namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A skeleton loader: shows a placeholder layout and, while <see cref="IsLoading"/> is true, sweeps
/// a translucent, tilted band across it. The band is clipped to the placeholder blocks, so only the
/// blocks light up and the gaps between them stay untouched.
/// </summary>
/// <remarks>
/// The blocks are every <see cref="BoxView"/>, every <see cref="Border"/> without content and every
/// childless element with a visible background. A block without a colour of its own is filled with
/// <see cref="ShimmerStyleOptions.PlaceholderColor"/>, which follows the theme. With Reduce Motion
/// on, the blocks stay and the wave does not run.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <Shimmer IsLoading="{Binding IsLoading}">
///     <VerticalStackLayout Spacing="10">
///         <Border StrokeThickness="0" HeightRequest="22" WidthRequest="300" HorizontalOptions="Start" />
///         <Border StrokeThickness="0" HeightRequest="16" WidthRequest="200" HorizontalOptions="Start" />
///     </VerticalStackLayout>
/// </Shimmer>
/// ]]></code>
/// </example>
[ContentProperty(nameof(SkeletonContent))]
public class Shimmer : ContentView
{
    public static readonly BindableProperty SkeletonContentProperty = BindableProperty.Create(
        nameof(SkeletonContent), typeof(View), typeof(Shimmer),
        propertyChanged: (b, o, n) => ((Shimmer)b).OnSkeletonContentChanged((View?)o, (View?)n));

    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(
        nameof(IsLoading), typeof(bool), typeof(Shimmer), false,
        propertyChanged: (b, _, n) => ((Shimmer)b)._overlay.IsLoading = (bool)n);

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(ShimmerStyleOptions), typeof(Shimmer),
        propertyChanged: (b, _, n) => ((Shimmer)b)._overlay.StyleOptions = (ShimmerStyleOptions?)n);

    public static readonly BindableProperty WaveWidthProperty = BindableProperty.Create(
        nameof(WaveWidth), typeof(double?), typeof(Shimmer),
        propertyChanged: (b, _, n) => ((Shimmer)b)._overlay.WaveWidth = (double?)n);

    public static readonly BindableProperty WaveOpacityProperty = BindableProperty.Create(
        nameof(WaveOpacity), typeof(double?), typeof(Shimmer),
        propertyChanged: (b, _, n) => ((Shimmer)b)._overlay.WaveOpacity = (double?)n);

    private readonly Grid _root;
    private readonly SkeletonOverlay _overlay;

    static Shimmer() => ShimmerStrings.EnsureRegistered();

    public Shimmer()
    {
        _overlay = new SkeletonOverlay(this, () => SkeletonContent);
        _root = new Grid();
        _root.Children.Add(_overlay);
        Content = _root;
    }

    /// <summary>The placeholder layout the wave sweeps across (the XAML content).</summary>
    public View? SkeletonContent
    {
        get => (View?)GetValue(SkeletonContentProperty);
        set => SetValue(SkeletonContentProperty, value);
    }

    /// <summary>Runs the wave while true. The placeholders show either way.</summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>This shimmer's look; unset, the app-wide <c>DefaultShimmerStyleOptions</c> and the theme.</summary>
    public ShimmerStyleOptions? StyleOptions
    {
        get => (ShimmerStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>
    /// Width of the band as a fraction of this shimmer's width, 0–1 (1 = as wide as the control). Wins
    /// over <see cref="ShimmerStyleOptions.WaveWidth"/> of <see cref="StyleOptions"/>, the app-wide options and the default.
    /// </summary>
    public double? WaveWidth
    {
        get => (double?)GetValue(WaveWidthProperty);
        set => SetValue(WaveWidthProperty, value);
    }

    /// <summary>
    /// Peak alpha at the centre of the band, 0–1: how strongly it lights the blocks. Wins over
    /// <see cref="ShimmerStyleOptions.WaveOpacity"/> of <see cref="StyleOptions"/>, the app-wide options and the default.
    /// </summary>
    public double? WaveOpacity
    {
        get => (double?)GetValue(WaveOpacityProperty);
        set => SetValue(WaveOpacityProperty, value);
    }

    /// <summary>
    /// Scans the placeholders again after the current layout pass. Size changes and added or removed
    /// views are noticed on their own; call this after changing a placeholder's colour or shape from code.
    /// </summary>
    public void RefreshPlaceholders() => _overlay.QueueRefresh();

    private void OnSkeletonContentChanged(View? oldView, View? newView)
    {
        if (oldView is not null)
            _root.Children.Remove(oldView);

        if (newView is not null)
        {
            // The blocks mean nothing to a screen reader; the overlay says "Loading" instead.
            AutomationProperties.SetExcludedWithChildren(newView, true);
            _root.Children.Insert(0, newView);
        }

        _overlay.QueueRefresh();
    }
}
