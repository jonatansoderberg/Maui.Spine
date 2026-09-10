namespace Plugin.Maui.Spine.Extensions;

/// <summary>How a <see cref="Button"/> or <see cref="ImageButton"/> is surfaced on platforms with a glass material.</summary>
public enum GlassStyle
{
    /// <summary>The platform's normal button.</summary>
    None,

    /// <summary>Frosted glass. The button keeps its <c>TextColor</c>; the surface comes from the system.</summary>
    Regular,

    /// <summary>Glass tinted with the button's <c>BackgroundColor</c>, for the one action a screen leads with.</summary>
    Prominent,

    /// <summary>Nearly transparent glass, for buttons floating over photos or maps.</summary>
    Clear,

    /// <summary>Tinted, nearly transparent glass.</summary>
    ProminentClear,

    /// <summary>
    /// No surface at rest; regular glass while the button is pressed. For an icon that floats on
    /// rich content and should not read as a control until it is touched.
    /// </summary>
    Transient,
}

/// <summary>
/// Renders a <see cref="Button"/> or <see cref="ImageButton"/> as Liquid Glass on iOS 26 and Mac Catalyst 26.
/// Everywhere else the attached property is ignored and the button looks as it always did.
/// </summary>
/// <remarks>
/// <para>
/// The glass takes over the surface: <c>BackgroundColor</c> (except as the tint of a prominent style),
/// <c>CornerRadius</c> and <c>BorderWidth</c> are not drawn. <c>Text</c>, font, <c>TextColor</c>, <c>Padding</c>,
/// <c>ContentLayout</c> and the image are honoured. An <see cref="ImageButton"/> shows its image at the size it was
/// rendered, so an SVG set through <c>SvgImageSource</c> gets its inset from <c>SvgImageSource.Padding</c>.
/// </para>
/// <example>
/// <code>
/// &lt;Button Text="Save" Glass.Style="Prominent" /&gt;
/// &lt;ImageButton SvgImageSource.Svg="settings.svg" SvgImageSource.Padding="10"
///              Glass.Style="Regular" WidthRequest="44" HeightRequest="44" /&gt;
/// </code>
/// </example>
/// </remarks>
public static class Glass
{
    internal const string MapperKey = "SpineGlass";

    /// <summary>Attached property holding the <see cref="GlassStyle"/> of a button.</summary>
    public static readonly BindableProperty StyleProperty =
        BindableProperty.CreateAttached(
            "Style",
            typeof(GlassStyle),
            typeof(Glass),
            GlassStyle.None,
            propertyChanged: static (bindable, _, _) => (bindable as VisualElement)?.Handler?.UpdateValue(MapperKey));

    /// <summary>Gets the <see cref="GlassStyle"/> of <paramref name="view"/>.</summary>
    public static GlassStyle GetStyle(BindableObject view) => (GlassStyle)view.GetValue(StyleProperty);

    /// <summary>Sets the <see cref="GlassStyle"/> of <paramref name="view"/>.</summary>
    public static void SetStyle(BindableObject view, GlassStyle value) => view.SetValue(StyleProperty, value);
}
