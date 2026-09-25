using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>What a <see cref="Material"/> does to what is behind a view.</summary>
public enum MaterialKind
{
    /// <summary>Nothing: what is behind shows as it is, under the tint.</summary>
    None,

    /// <summary>
    /// What is behind, blurred as much as <see cref="Material.IntensityProperty"/> asks: the thinnest
    /// system material on iOS and Mac Catalyst, acrylic on Windows, a blurred snapshot on Android 12 and
    /// later. The tint alone on older Android.
    /// </summary>
    Blur,

    /// <summary>
    /// Liquid Glass on iOS and Mac Catalyst 26: refracts what is behind it and can react to touch, clear at
    /// intensity 0 and frosted (the system's regular glass) at 1. For controls that float over content;
    /// <see cref="Blur"/> everywhere else.
    /// </summary>
    Glass,
}

/// <summary>
/// Named materials, from clear to thick, named as Apple names its own. A preset is only a set of values: the
/// <see cref="Material.KindProperty"/>, <see cref="Material.IntensityProperty"/> and
/// <see cref="Material.TintOpacityProperty"/> a view gets unless it sets them itself.
/// </summary>
public enum MaterialPreset
{
    /// <summary>No preset: no material unless the view sets one.</summary>
    None,

    /// <summary>Clear glass: <see cref="MaterialKind.Glass"/>, intensity 0.</summary>
    GlassClear,

    /// <summary>Regular, frosted glass: <see cref="MaterialKind.Glass"/>, intensity 1.</summary>
    GlassRegular,

    /// <summary>A light blur: <see cref="MaterialKind.Blur"/>, intensity 0.3, no tint.</summary>
    BlurUltraThin,

    /// <summary>More blur and a little milk: <see cref="MaterialKind.Blur"/>, intensity 0.55, tint opacity 0.15.</summary>
    BlurThin,

    /// <summary>The standard panel: <see cref="MaterialKind.Blur"/>, intensity 0.8, tint opacity 0.3.</summary>
    BlurRegular,

    /// <summary>Full blur with the most milk, for dense text: <see cref="MaterialKind.Blur"/>, intensity 1, tint opacity 0.45.</summary>
    BlurThick,
}

/// <summary>The system materials the header bar's scroll edge is tuned against on iOS.</summary>
internal enum SystemBlur
{
    None,
    Thin,
    Regular,
}

/// <summary>
/// Gives a <see cref="Border"/>, a <see cref="ContentView"/> or a layout a material behind its content,
/// in two layers: what happens to what is behind (<see cref="KindProperty"/>, as strongly as
/// <see cref="IntensityProperty"/>), and a colour over it (<see cref="TintProperty"/>, as opaque as
/// <see cref="TintOpacityProperty"/>). The shape comes from <see cref="Border.StrokeShape"/>.
/// </summary>
/// <remarks>
/// <para>
/// Apple's guidance: glass belongs to controls floating over content (bars, floating buttons); panels
/// that hold content use <see cref="MaterialKind.Blur"/> or a tint alone; never glass on glass. Leave the
/// view's own <c>Background</c> unset, or the material is drawn over it.
/// </para>
/// <para>
/// With Reduce Transparency (iOS, Mac Catalyst) or transparency effects off (Windows) the system
/// materials turn opaque by themselves, and so does a tint without a blur.
/// </para>
/// <example>
/// <code>
/// &lt;Border Material.Kind="Blur" Material.TintOpacity="0.5" StrokeThickness="0" StrokeShape="RoundRectangle 16"&gt;
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

    static object Unit(BindableObject _, object value) => Math.Clamp((double)value, 0, 1);

    /// <summary>Attached property holding what the material does to what is behind. Default: the preset's, or <see cref="MaterialKind.None"/>.</summary>
    public static readonly BindableProperty KindProperty = BindableProperty.CreateAttached(
        "Kind", typeof(MaterialKind), typeof(Material), MaterialKind.None, propertyChanged: Remap);

    /// <summary>Gets the <see cref="MaterialKind"/> of <paramref name="view"/>: its own, or else its preset's.</summary>
    public static MaterialKind GetKind(BindableObject view) =>
        view.IsSet(KindProperty) ? (MaterialKind)view.GetValue(KindProperty) : Values(GetPreset(view)).Kind;

    /// <summary>Sets the <see cref="MaterialKind"/> of <paramref name="view"/>.</summary>
    public static void SetKind(BindableObject view, MaterialKind value) => view.SetValue(KindProperty, value);

    /// <summary>
    /// Attached property holding how frosted what is behind becomes, from 0 to 1: a blur from none to
    /// full, glass from clear to regular. Default: the preset's, or 1.
    /// </summary>
    public static readonly BindableProperty IntensityProperty = BindableProperty.CreateAttached(
        "Intensity", typeof(double), typeof(Material), 1d, propertyChanged: Remap, coerceValue: Unit);

    /// <summary>Gets how frosted <paramref name="view"/>'s material is, from 0 to 1: its own, or else its preset's.</summary>
    public static double GetIntensity(BindableObject view) =>
        view.IsSet(IntensityProperty) ? (double)view.GetValue(IntensityProperty) : Values(GetPreset(view)).Intensity;

    /// <summary>Sets how frosted <paramref name="view"/>'s material is, from 0 to 1.</summary>
    public static void SetIntensity(BindableObject view, double value) => view.SetValue(IntensityProperty, value);

    /// <summary>
    /// Attached property holding the colour over the material; when unset, the theme's surface: white in
    /// light mode, near-black (#1C1C1E) in dark, the colour of cards and sheets. How much of it shows is <see cref="TintOpacityProperty"/>; on glass it tints the glass.
    /// </summary>
    public static readonly BindableProperty TintProperty = BindableProperty.CreateAttached(
        "Tint", typeof(Color), typeof(Material), null, propertyChanged: Remap);

    /// <summary>Gets the tint of <paramref name="view"/>'s material; <see langword="null"/> for the theme's surface.</summary>
    public static Color? GetTint(BindableObject view) => (Color?)view.GetValue(TintProperty);

    /// <summary>Sets the tint of <paramref name="view"/>'s material; <see langword="null"/> for the theme's surface.</summary>
    public static void SetTint(BindableObject view, Color? value) => view.SetValue(TintProperty, value);

    /// <summary>
    /// Attached property holding how much of the tint shows, from 0 (none) to 1 (opaque, as a solid
    /// surface): the milk of the system's thicker materials over a blur, or a tinted panel on its own.
    /// Default: the preset's, or 0.
    /// </summary>
    public static readonly BindableProperty TintOpacityProperty = BindableProperty.CreateAttached(
        "TintOpacity", typeof(double), typeof(Material), 0d, propertyChanged: Remap, coerceValue: Unit);

    /// <summary>Gets how much of <paramref name="view"/>'s tint shows, from 0 to 1: its own, or else its preset's.</summary>
    public static double GetTintOpacity(BindableObject view) =>
        view.IsSet(TintOpacityProperty) ? (double)view.GetValue(TintOpacityProperty) : Values(GetPreset(view)).TintOpacity;

    /// <summary>Sets how much of <paramref name="view"/>'s tint shows, from 0 to 1.</summary>
    public static void SetTintOpacity(BindableObject view, double value) => view.SetValue(TintOpacityProperty, value);

    /// <summary>
    /// Attached property holding one of the system's own materials: the kind, intensity and tint opacity
    /// the view gets unless it sets them itself. <c>Material.Preset="BlurThin"</c> needs nothing else.
    /// </summary>
    public static readonly BindableProperty PresetProperty = BindableProperty.CreateAttached(
        "Preset", typeof(MaterialPreset), typeof(Material), MaterialPreset.None, propertyChanged: Remap);

    /// <summary>Gets the <see cref="MaterialPreset"/> of <paramref name="view"/>.</summary>
    public static MaterialPreset GetPreset(BindableObject view) => (MaterialPreset)view.GetValue(PresetProperty);

    /// <summary>Sets the <see cref="MaterialPreset"/> of <paramref name="view"/>.</summary>
    public static void SetPreset(BindableObject view, MaterialPreset value) => view.SetValue(PresetProperty, value);

    /// <summary>The values <paramref name="preset"/> stands for.</summary>
    public static (MaterialKind Kind, double Intensity, double TintOpacity) Values(MaterialPreset preset) => preset switch
    {
        MaterialPreset.GlassClear => (MaterialKind.Glass, 0, 0),
        MaterialPreset.GlassRegular => (MaterialKind.Glass, 1, 0),
        MaterialPreset.BlurUltraThin => (MaterialKind.Blur, 0.3, 0),
        MaterialPreset.BlurThin => (MaterialKind.Blur, 0.55, 0.15),
        MaterialPreset.BlurRegular => (MaterialKind.Blur, 0.8, 0.3),
        MaterialPreset.BlurThick => (MaterialKind.Blur, 1, 0.45),
        _ => (MaterialKind.None, 1, 0),
    };

    /// <summary>Whether glass reacts to touch, as iOS 26 glass controls do. Glass on iOS and Mac Catalyst 26 only.</summary>
    public static readonly BindableProperty InteractiveProperty = BindableProperty.CreateAttached(
        "Interactive", typeof(bool), typeof(Material), false, propertyChanged: Remap);

    /// <summary>Gets whether <paramref name="view"/>'s glass reacts to touch.</summary>
    public static bool GetInteractive(BindableObject view) => (bool)view.GetValue(InteractiveProperty);

    /// <summary>Sets whether <paramref name="view"/>'s glass reacts to touch.</summary>
    public static void SetInteractive(BindableObject view, bool value) => view.SetValue(InteractiveProperty, value);

    /// <summary>The system material in place of the thinnest one: the header bar's scroll edge on iOS.</summary>
    internal static readonly BindableProperty SystemBlurProperty = BindableProperty.CreateAttached(
        "SystemBlur", typeof(SystemBlur), typeof(Material), SystemBlur.None, propertyChanged: Remap);

    internal static SystemBlur GetSystemBlur(BindableObject view) => (SystemBlur)view.GetValue(SystemBlurProperty);

    internal static void SetSystemBlur(BindableObject view, SystemBlur value) => view.SetValue(SystemBlurProperty, value);

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

    /// <summary>Whether <paramref name="view"/> has a material at all: something done to what is behind, or a tint.</summary>
    internal static bool IsOn(BindableObject view) => GetKind(view) != MaterialKind.None || GetTintOpacity(view) > 0;

    /// <summary>
    /// What <paramref name="kind"/> is drawn as here: glass only where the system has it, blur only
    /// where the platform can blur what is behind a view, and else the tint alone.
    /// </summary>
    internal static MaterialKind Resolve(MaterialKind kind) => kind switch
    {
        MaterialKind.Glass when HasGlass => kind,
        MaterialKind.Glass or MaterialKind.Blur => HasBlur ? MaterialKind.Blur : MaterialKind.None,
        _ => kind,
    };

    /// <summary>How strongly a blur that stands in for glass blurs: clear glass still frosts a little.</summary>
    internal static double BlurIntensity(BindableObject view) =>
        GetKind(view) == MaterialKind.Glass ? 0.3 + 0.7 * GetIntensity(view) : GetIntensity(view);

    /// <summary>Whether Liquid Glass exists: iOS and Mac Catalyst 26.</summary>
    internal static bool HasGlass =>
        OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);

    /// <summary>Whether the platform can blur what is behind a view: not on Android before 12.</summary>
    internal static bool HasBlur => !OperatingSystem.IsAndroid() || OperatingSystem.IsAndroidVersionAtLeast(31);

    /// <summary>How opaque the tint is at least where a blur or glass cannot be drawn, so the panel still reads.</summary>
    internal const float StandInTintOpacity = 0.72f;

    /// <summary>
    /// The tint as it is drawn: its colour (the theme's surface when unset) as opaque as its opacity asks,
    /// at least <see cref="StandInTintOpacity"/> where it stands in for a blur the platform cannot draw,
    /// and opaque with transparency reduced where nothing else is drawn. <see langword="null"/> when none shows.
    /// </summary>
    internal static Color? TintLayer(VisualElement owner)
    {
        var colour = GetTint(owner) ?? Surface();
        var opacity = GetTintOpacity(owner);
        var kind = GetKind(owner);
        var drawn = Resolve(kind);

        if (drawn == MaterialKind.None && kind != MaterialKind.None)
            opacity = Math.Max(opacity, StandInTintOpacity);
        if (drawn == MaterialKind.None && ReducedTransparency.IsOn && opacity > 0)
            opacity = 1;

        var alpha = (float)(colour.Alpha * opacity);
        return alpha > 0 ? colour.WithAlpha(alpha) : null;
    }

    /// <summary><paramref name="top"/> drawn over <paramref name="bottom"/>, as one colour.</summary>
    internal static Color? Over(Color? top, Color? bottom)
    {
        if (top is null || bottom is null)
            return top ?? bottom;

        var alpha = top.Alpha + bottom.Alpha * (1 - top.Alpha);
        if (alpha <= 0)
            return Colors.Transparent;

        float Mix(float t, float b) => (t * top.Alpha + b * bottom.Alpha * (1 - top.Alpha)) / alpha;
        return new Color(Mix(top.Red, bottom.Red), Mix(top.Green, bottom.Green), Mix(top.Blue, bottom.Blue), alpha);
    }

    /// <summary>The theme's surface colour.</summary>
    internal static Color Surface() => IsDark() ? Color.FromRgb(28, 28, 30) : Colors.White;

    internal static bool IsDark() =>
        Application.Current?.RequestedTheme == AppTheme.Dark
        || (Application.Current?.RequestedTheme != AppTheme.Light
            && Application.Current?.PlatformAppTheme == AppTheme.Dark);
}

/// <summary>
/// Holds glass surfaces that belong together, so they share one piece of glass: on iOS and Mac
/// Catalyst 26, surfaces within <see cref="Spacing"/> of each other merge, and pull apart as they move
/// away, as the system's own bar buttons do. Everywhere else an ordinary <see cref="ContentView"/>.
/// </summary>
/// <example>
/// <code>
/// &lt;MaterialContainer Spacing="16"&gt;
///     &lt;HorizontalStackLayout Spacing="8"&gt;
///         &lt;Border Material.Kind="Glass" StrokeShape="RoundRectangle 22" /&gt;
///         &lt;Border Material.Kind="Glass" StrokeShape="RoundRectangle 22" /&gt;
///     &lt;/HorizontalStackLayout&gt;
/// &lt;/MaterialContainer&gt;
/// </code>
/// </example>
public class MaterialContainer : ContentView
{
    /// <summary>How close two glass surfaces come before they merge. Default 20.</summary>
    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(
        nameof(Spacing), typeof(double), typeof(MaterialContainer), 20d,
        propertyChanged: static (bindable, _, _) => ((VisualElement)bindable).Handler?.UpdateValue(Material.MapperKey));

    /// <summary>How close two glass surfaces come before they merge.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}
