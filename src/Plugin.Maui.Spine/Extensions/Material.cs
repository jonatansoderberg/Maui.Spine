using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>The surface <see cref="Material"/> gives a <see cref="Border"/>, <see cref="ContentView"/> or layout.</summary>
public enum MaterialKind
{
    /// <summary>No material: the view draws its own background.</summary>
    None,

    /// <summary>
    /// Liquid Glass on iOS and Mac Catalyst 26: refracts what is behind it and can react to touch.
    /// For controls that float over content; <see cref="Blur"/> everywhere else.
    /// </summary>
    Glass,

    /// <summary>
    /// What is behind the view, blurred: a system material on iOS and Mac Catalyst, acrylic on
    /// Windows, a blurred snapshot on Android 12 and later. <see cref="Tinted"/> on older Android.
    /// </summary>
    Blur,

    /// <summary>The theme's surface colour, slightly see-through. No blur on any platform.</summary>
    Tinted,

    /// <summary>The theme's surface colour, opaque.</summary>
    Solid,
}

/// <summary>How much of what is behind a <see cref="MaterialKind.Blur"/> comes through.</summary>
public enum MaterialThickness
{
    /// <summary>Most of it: the lightest material.</summary>
    UltraThin,

    /// <summary>Much of it.</summary>
    Thin,

    /// <summary>The system's standard material.</summary>
    Regular,

    /// <summary>Little of it.</summary>
    Thick,

    /// <summary>Almost none of it, as behind a system bar.</summary>
    Chrome,
}

/// <summary>
/// Gives a <see cref="Border"/>, a <see cref="ContentView"/> or a layout a platform material behind its
/// content: glass, blur, a tinted or a solid surface. The shape comes from <see cref="Border.StrokeShape"/>.
/// </summary>
/// <remarks>
/// <para>
/// Apple's guidance: glass belongs to controls floating over content (bars, floating buttons); panels
/// that hold content use <see cref="MaterialKind.Blur"/> or <see cref="MaterialKind.Tinted"/>; never
/// glass on glass. Leave the view's own <c>Background</c> unset, or the material is drawn over it.
/// </para>
/// <para>
/// With Reduce Transparency (iOS, Mac Catalyst) or transparency effects off (Windows) the system
/// materials turn opaque by themselves, and <see cref="MaterialKind.Tinted"/> becomes
/// <see cref="MaterialKind.Solid"/>.
/// </para>
/// <example>
/// <code>
/// &lt;Border Material.Kind="Blur" StrokeThickness="0" StrokeShape="RoundRectangle 16"&gt;
///     &lt;Label Text="Over the photo" /&gt;
/// &lt;/Border&gt;
/// </code>
/// </example>
/// </remarks>
public static class Material
{
    internal const string MapperKey = "SpineMaterial";

    static void Remap(BindableObject bindable, object oldValue, object newValue) =>
        (bindable as VisualElement)?.Handler?.UpdateValue(MapperKey);

    /// <summary>Attached property holding the <see cref="MaterialKind"/> of a view.</summary>
    public static readonly BindableProperty KindProperty = BindableProperty.CreateAttached(
        "Kind", typeof(MaterialKind), typeof(Material), MaterialKind.None, propertyChanged: Remap);

    /// <summary>Gets the <see cref="MaterialKind"/> of <paramref name="view"/>.</summary>
    public static MaterialKind GetKind(BindableObject view) => (MaterialKind)view.GetValue(KindProperty);

    /// <summary>Sets the <see cref="MaterialKind"/> of <paramref name="view"/>.</summary>
    public static void SetKind(BindableObject view, MaterialKind value) => view.SetValue(KindProperty, value);

    /// <summary>Attached property holding the <see cref="MaterialThickness"/> of a blur. Default <see cref="MaterialThickness.Regular"/>.</summary>
    public static readonly BindableProperty ThicknessProperty = BindableProperty.CreateAttached(
        "Thickness", typeof(MaterialThickness), typeof(Material), MaterialThickness.Regular, propertyChanged: Remap);

    /// <summary>Gets the <see cref="MaterialThickness"/> of <paramref name="view"/>.</summary>
    public static MaterialThickness GetThickness(BindableObject view) => (MaterialThickness)view.GetValue(ThicknessProperty);

    /// <summary>Sets the <see cref="MaterialThickness"/> of <paramref name="view"/>.</summary>
    public static void SetThickness(BindableObject view, MaterialThickness value) => view.SetValue(ThicknessProperty, value);

    /// <summary>
    /// A colour bled into the material: the tint of glass, a layer over a blur (give it some
    /// transparency), or the colour of a tinted or solid surface instead of the theme's.
    /// </summary>
    public static readonly BindableProperty TintProperty = BindableProperty.CreateAttached(
        "Tint", typeof(Color), typeof(Material), null, propertyChanged: Remap);

    /// <summary>Gets the tint of <paramref name="view"/>'s material.</summary>
    public static Color? GetTint(BindableObject view) => (Color?)view.GetValue(TintProperty);

    /// <summary>Sets the tint of <paramref name="view"/>'s material.</summary>
    public static void SetTint(BindableObject view, Color? value) => view.SetValue(TintProperty, value);

    /// <summary>Whether glass reacts to touch, as iOS 26 glass controls do. Glass on iOS and Mac Catalyst 26 only.</summary>
    public static readonly BindableProperty InteractiveProperty = BindableProperty.CreateAttached(
        "Interactive", typeof(bool), typeof(Material), false, propertyChanged: Remap);

    /// <summary>Gets whether <paramref name="view"/>'s glass reacts to touch.</summary>
    public static bool GetInteractive(BindableObject view) => (bool)view.GetValue(InteractiveProperty);

    /// <summary>Sets whether <paramref name="view"/>'s glass reacts to touch.</summary>
    public static void SetInteractive(BindableObject view, bool value) => view.SetValue(InteractiveProperty, value);

    /// <summary>How far the material fades out at its bottom edge, in points: the header bar's edge.</summary>
    internal static readonly BindableProperty FadeProperty = BindableProperty.CreateAttached(
        "Fade", typeof(double), typeof(Material), 0d, propertyChanged: Remap);

    internal static double GetFade(BindableObject view) => (double)view.GetValue(FadeProperty);

    internal static void SetFade(BindableObject view, double value) => view.SetValue(FadeProperty, value);

    /// <summary>A one-pixel line along the material's bottom edge: the header bar's hairline.</summary>
    internal static readonly BindableProperty EdgeLineProperty = BindableProperty.CreateAttached(
        "EdgeLine", typeof(Color), typeof(Material), null, propertyChanged: Remap);

    internal static Color? GetEdgeLine(BindableObject view) => (Color?)view.GetValue(EdgeLineProperty);

    internal static void SetEdgeLine(BindableObject view, Color? value) => view.SetValue(EdgeLineProperty, value);

    /// <summary>
    /// What <paramref name="kind"/> is drawn as here: glass only where the system has it, blur only
    /// where the platform can blur what is behind a view, tinted only with transparency allowed.
    /// </summary>
    internal static MaterialKind Resolve(MaterialKind kind) => kind switch
    {
        MaterialKind.Glass when HasGlass => MaterialKind.Glass,
        MaterialKind.Glass or MaterialKind.Blur => HasBlur ? MaterialKind.Blur : Resolve(MaterialKind.Tinted),
        MaterialKind.Tinted when ReducedTransparency.IsOn => MaterialKind.Solid,
        _ => kind,
    };

    /// <summary>Whether Liquid Glass exists: iOS and Mac Catalyst 26.</summary>
    internal static bool HasGlass =>
        OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

    /// <summary>Whether the platform can blur what is behind a view: not on Android yet.</summary>
    internal static bool HasBlur => !OperatingSystem.IsAndroid();

    /// <summary>How see-through a tinted surface is.</summary>
    internal const float TintedAlpha = 0.72f;

    /// <summary>The colour of a tinted or solid surface: the tint, or the theme's surface.</summary>
    internal static Color SurfaceColour(Color? tint, bool tinted)
    {
        var colour = tint ?? (IsDark() ? Color.FromRgb(28, 28, 30) : Colors.White);
        if (!tinted)
            return colour.WithAlpha(1);

        return tint is null ? colour.WithAlpha(TintedAlpha) : colour;
    }

    internal static bool IsDark() =>
        Application.Current?.RequestedTheme == AppTheme.Dark
        || (Application.Current?.RequestedTheme != AppTheme.Light
            && Application.Current?.PlatformAppTheme == AppTheme.Dark);
}
