namespace Plugin.Maui.SvgImage;

/// <summary>
/// Provides XAML-friendly attached properties that drive SVG image rendering on
/// <see cref="Image"/> and <see cref="ImageButton"/> controls.
/// </summary>
/// <remarks>
/// <para>
/// The simplest way to use this class from XAML is to set
/// <c>SvgImageSource.EnableSvg="True"</c> and <c>SvgImageSource.Svg="icon.svg"</c>
/// on any <see cref="Image"/> or <see cref="ImageButton"/>. The helper automatically
/// attaches a <see cref="SvgImageSourceBehavior"/> and keeps the rendered image in sync
/// with the control size and the current app theme.
/// </para>
/// <para>
/// Alternatively, you can add <see cref="SvgImageSourceBehavior"/> directly as a
/// <c>Behavior</c> in XAML for finer control.
/// </para>
/// </remarks>
public static class SvgImageSource
{
    /// <summary>
    /// Identifies the <c>Svg</c> attached property.
    /// </summary>
    public static readonly BindableProperty SvgProperty =
        BindableProperty.CreateAttached(
            "Svg",
            typeof(string),
            typeof(SvgImageSource),
            default(string),
            propertyChanged: OnAttachedValueChanged);

    /// <summary>Sets the short SVG resource name (e.g. <c>"icon.svg"</c>) on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">The SVG resource name to set.</param>
    public static void SetSvg(BindableObject obj, string value)
        => obj.SetValue(SvgProperty, value);

    /// <summary>Gets the short SVG resource name currently set on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns>The SVG resource name, or <see langword="null"/> if not set.</returns>
    public static string GetSvg(BindableObject obj)
        => (string)obj.GetValue(SvgProperty);



    /// <summary>
    /// Identifies the <c>TintColor</c> attached property.
    /// An absolute tint colour that overrides <see cref="LightTintColorProperty"/> and
    /// <see cref="DarkTintColorProperty"/> regardless of the current app theme.
    /// When <see langword="null"/> (the default), the tint is resolved from the theme-aware properties.
    /// </summary>
    public static readonly BindableProperty TintColorProperty =
        BindableProperty.CreateAttached(
            "TintColor",
            typeof(Color),
            typeof(SvgImageSource),
            null,
            propertyChanged: OnAttachedValueChanged);

    /// <summary>Sets the absolute tint colour on <paramref name="obj"/>, overriding the theme-aware tint colours.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">The <see cref="Color"/> to apply, or <see langword="null"/> to use theme-aware colours.</param>
    public static void SetTintColor(BindableObject obj, Color value)
        => obj.SetValue(TintColorProperty, value);

    /// <summary>Gets the absolute tint colour currently set on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns>The tint <see cref="Color"/>, or <see langword="null"/> if not set.</returns>
    public static Color? GetTintColor(BindableObject obj)
        => (Color?)obj.GetValue(TintColorProperty);



    /// <summary>
    /// Identifies the <c>LightTintColor</c> attached property.
    /// The tint colour applied when the app is using the <see cref="AppTheme.Light"/> theme.
    /// Defaults to <see cref="Colors.Transparent"/> (no tint).
    /// </summary>
    public static readonly BindableProperty LightTintColorProperty =
        BindableProperty.CreateAttached(
            "LightTintColor",
            typeof(Color),
            typeof(SvgImageSource),
            Colors.Black,
            propertyChanged: OnAttachedValueChanged);

    /// <summary>Sets the light-theme tint colour on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">The <see cref="Color"/> to apply in light theme.</param>
    public static void SetLightTintColor(BindableObject obj, Color value)
        => obj.SetValue(LightTintColorProperty, value);

    /// <summary>Gets the light-theme tint colour currently set on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns>The light-theme tint <see cref="Color"/>.</returns>
    public static Color GetLightTintColor(BindableObject obj)
        => (Color)obj.GetValue(LightTintColorProperty);



    /// <summary>
    /// Identifies the <c>DarkTintColor</c> attached property.
    /// The tint colour applied when the app is using the <see cref="AppTheme.Dark"/> theme.
    /// Defaults to <see cref="Colors.Transparent"/> (no tint).
    /// </summary>
    public static readonly BindableProperty DarkTintColorProperty =
        BindableProperty.CreateAttached(
            "DarkTintColor",
            typeof(Color),
            typeof(SvgImageSource),
            Colors.White,
            propertyChanged: OnAttachedValueChanged);

    /// <summary>Sets the dark-theme tint colour on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">The <see cref="Color"/> to apply in dark theme.</param>
    public static void SetDarkTintColor(BindableObject obj, Color value)
        => obj.SetValue(DarkTintColorProperty, value);

    /// <summary>Gets the dark-theme tint colour currently set on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns>The dark-theme tint <see cref="Color"/>.</returns>
    public static Color GetDarkTintColor(BindableObject obj)
        => (Color)obj.GetValue(DarkTintColorProperty);



    /// <summary>
    /// Identifies the <c>Padding</c> attached property.
    /// Controls the inset padding (in pixels) applied around the SVG within the canvas.
    /// Defaults to a uniform padding of <c>5</c>.
    /// </summary>
    public static readonly BindableProperty PaddingProperty =
        BindableProperty.CreateAttached(
            "Padding",
            typeof(Thickness),
            typeof(SvgImageSource),
            new Thickness(5),
            propertyChanged: OnAttachedValueChanged);

    /// <summary>Sets the SVG canvas padding on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">The <see cref="Thickness"/> padding to apply.</param>
    public static void SetPadding(BindableObject obj, Thickness value)
        => obj.SetValue(PaddingProperty, value);

    /// <summary>Gets the SVG canvas padding currently set on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns>The current <see cref="Thickness"/> padding.</returns>
    public static Thickness GetPadding(BindableObject obj)
        => (Thickness)obj.GetValue(PaddingProperty);



    /// <summary>
    /// Identifies the <c>EnableSvg</c> attached property.
    /// When set to <see langword="true"/>, a <see cref="SvgImageSourceBehavior"/> is automatically
    /// attached to the target <see cref="View"/> and kept in sync with the other SVG attached
    /// properties. Defaults to <see langword="false"/>.
    /// </summary>
    public static readonly BindableProperty EnableSvgProperty =
        BindableProperty.CreateAttached(
            "EnableSvg",
            typeof(bool),
            typeof(SvgImageSource),
            false,
            propertyChanged: OnEnableChanged);

    /// <summary>Enables or disables SVG rendering on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <param name="value">
    /// <see langword="true"/> to attach <see cref="SvgImageSourceBehavior"/> and start rendering;
    /// <see langword="false"/> to detach it.
    /// </param>
    public static void SetEnableSvg(BindableObject obj, bool value)
        => obj.SetValue(EnableSvgProperty, value);

    /// <summary>Gets whether SVG rendering is currently enabled on <paramref name="obj"/>.</summary>
    /// <param name="obj">The target <see cref="BindableObject"/>.</param>
    /// <returns><see langword="true"/> if SVG rendering is active; otherwise <see langword="false"/>.</returns>
    public static bool GetEnableSvg(BindableObject obj)
        => (bool)obj.GetValue(EnableSvgProperty);



    private static readonly BindableProperty BehaviorInstanceProperty =
        BindableProperty.CreateAttached(
            "BehaviorInstance",
            typeof(SvgImageSourceBehavior),
            typeof(SvgImageSource),
            null);

    private static SvgImageSourceBehavior? GetBehaviorInstance(BindableObject obj)
        => (SvgImageSourceBehavior?)obj.GetValue(BehaviorInstanceProperty);

    private static void SetBehaviorInstance(BindableObject obj, SvgImageSourceBehavior? value)
        => obj.SetValue(BehaviorInstanceProperty, value);



    private static void OnEnableChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        bool enable = (bool)newValue;
        var existing = GetBehaviorInstance(bindable);

        if (enable && existing is null)
        {
            // Auto attach new behavior
            var behavior = new SvgImageSourceBehavior();
            view.Behaviors.Add(behavior);
            SetBehaviorInstance(bindable, behavior);
            ApplyValues(bindable, behavior);
        }
        else if (!enable && existing != null)
        {
            if (view.Behaviors.Contains(existing))
                view.Behaviors.Remove(existing);
            SetBehaviorInstance(bindable, null);
        }
    }

    private static void OnAttachedValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var behavior = GetBehaviorInstance(bindable);

        // Auto-attach behavior when an SVG name is present, even without EnableSvg="True"
        if (behavior == null && !string.IsNullOrWhiteSpace(GetSvg(bindable)))
            OnEnableChanged(bindable, false, true);

        behavior = GetBehaviorInstance(bindable);
        if (behavior != null)
            ApplyValues(bindable, behavior);
    }

    private static void ApplyValues(BindableObject bindable, SvgImageSourceBehavior behavior)
    {
        behavior.Svg = GetSvg(bindable);
        behavior.TintColor = GetTintColor(bindable);
        behavior.LightTintColor = GetLightTintColor(bindable);
        behavior.DarkTintColor = GetDarkTintColor(bindable);
        behavior.Padding = GetPadding(bindable);
        behavior.UpdateImage();
    }
}
