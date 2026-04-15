namespace Plugin.Maui.SvgImage;

/// <summary>
/// A MAUI <see cref="Behavior{T}"/> that renders an embedded SVG resource as the
/// <see cref="ImageSource"/> of an <see cref="Image"/> or <see cref="ImageButton"/>.
/// </summary>
/// <remarks>
/// <para>
/// Attach this behavior directly in XAML or let <see cref="SvgImageSource.EnableSvgProperty"/>
/// attach it automatically. The behavior re-renders the SVG whenever the control is resized
/// or the application theme changes.
/// </para>
/// <para>
/// SVG resources are resolved via the <see cref="ResourceNameCache"/> singleton registered
/// by <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages"/>.
/// </para>
/// </remarks>
public class SvgImageSourceBehavior : Behavior<View>
{
    private View? _associatedView;
    private ResourceNameCache? _svgRegistry;

    /// <summary>Identifies the <see cref="Svg"/> bindable property.</summary>
    public static readonly BindableProperty SvgProperty =
        BindableProperty.Create(nameof(Svg), typeof(string), typeof(SvgImageSourceBehavior), null,
            propertyChanged: static (b, _, _) => ((SvgImageSourceBehavior)b).UpdateImage());

    /// <summary>Identifies the <see cref="LightTintColor"/> bindable property.</summary>
    public static readonly BindableProperty LightTintColorProperty =
        BindableProperty.Create(nameof(LightTintColor), typeof(Color), typeof(SvgImageSourceBehavior), Colors.Transparent,
            propertyChanged: static (b, _, _) => ((SvgImageSourceBehavior)b).UpdateImage());

    /// <summary>Identifies the <see cref="DarkTintColor"/> bindable property.</summary>
    public static readonly BindableProperty DarkTintColorProperty =
        BindableProperty.Create(nameof(DarkTintColor), typeof(Color), typeof(SvgImageSourceBehavior), Colors.Transparent,
            propertyChanged: static (b, _, _) => ((SvgImageSourceBehavior)b).UpdateImage());

    /// <summary>Identifies the <see cref="TintColor"/> bindable property.</summary>
    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(SvgImageSourceBehavior), null,
            propertyChanged: static (b, _, _) => ((SvgImageSourceBehavior)b).UpdateImage());

    /// <summary>Identifies the <see cref="Padding"/> bindable property.</summary>
    public static readonly BindableProperty PaddingProperty =
        BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(SvgImageSourceBehavior), new Thickness(5),
            propertyChanged: static (b, _, _) => ((SvgImageSourceBehavior)b).UpdateImage());

    /// <summary>
    /// Gets or sets the short SVG resource name to render (e.g. <c>"icon.svg"</c>).
    /// </summary>
    public string Svg
    {
        get => (string)GetValue(SvgProperty);
        set => SetValue(SvgProperty, value);
    }

    /// <summary>
    /// Gets or sets the tint colour applied to the SVG when the app is in
    /// <see cref="AppTheme.Light"/> mode. Defaults to <see cref="Colors.Transparent"/> (no tint).
    /// </summary>
    public Color LightTintColor
    {
        get => (Color)GetValue(LightTintColorProperty);
        set => SetValue(LightTintColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the tint colour applied to the SVG when the app is in
    /// <see cref="AppTheme.Dark"/> mode. Defaults to <see cref="Colors.Transparent"/> (no tint).
    /// </summary>
    public Color DarkTintColor
    {
        get => (Color)GetValue(DarkTintColorProperty);
        set => SetValue(DarkTintColorProperty, value);
    }

    /// <summary>
    /// Gets or sets an absolute tint colour that overrides <see cref="LightTintColor"/> and
    /// <see cref="DarkTintColor"/> regardless of the current app theme. When <see langword="null"/>
    /// (the default), the tint is resolved from the theme-aware properties.
    /// </summary>
    public Color? TintColor
    {
        get => (Color?)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding (in pixels) inset from each edge of the canvas before the SVG
    /// is drawn. Defaults to a uniform padding of <c>5</c>.
    /// </summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedView = bindable;

        _svgRegistry = IPlatformApplication.Current?.Services
            .GetService<ResourceNameCache>();

        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;

        _associatedView.SizeChanged += OnViewSizeChanged;

        UpdateImage();
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);

        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged -= OnRequestedThemeChanged;

        if (_associatedView is not null)
            _associatedView.SizeChanged -= OnViewSizeChanged;

        _associatedView = null;
        _svgRegistry = null;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        UpdateImage();
    }

    private void OnViewSizeChanged(object? sender, EventArgs e)
    {
        UpdateImage();
    }

    /// <summary>
    /// Re-renders the SVG at the current size and theme and updates the image source
    /// of the associated <see cref="Image"/> or <see cref="ImageButton"/>.
    /// </summary>
    /// <remarks>
    /// This method is called automatically when the view is resized or the app theme changes.
    /// Call it manually only if you change a property (e.g. <see cref="Svg"/>) programmatically
    /// and need an immediate refresh outside of the normal property-change cycle.
    /// </remarks>
    public async void UpdateImage()
    {
        if (_associatedView is null)
            return;

        if (string.IsNullOrWhiteSpace(Svg))
        {
            SetSource(null);
            return;
        }

        var width = _associatedView.Width > 0 ? _associatedView.Width : _associatedView.WidthRequest;
        var height = _associatedView.Height > 0 ? _associatedView.Height : _associatedView.HeightRequest;

        if (width <= 0 && height <= 0)
            width = height = 32;
        else if (width <= 0)
            width = height;
        else if (height <= 0)
            height = width;

        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var tint = TintColor ?? (theme == AppTheme.Dark ? DarkTintColor : LightTintColor);

        var resourceName = _svgRegistry?.Resolve(Svg) ?? Svg;
        var source = SvgBitmapLoader.LoadFromEmbedded(resourceName, width, height, tint, Padding);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_associatedView is not null)
                SetSource(source);
        });
    }

    private void SetSource(ImageSource? source)
    {
        switch (_associatedView)
        {
            case Image img:
                img.Source = source;
                break;
            case ImageButton btn:
                btn.Source = source;
                break;
        }
    }
}
