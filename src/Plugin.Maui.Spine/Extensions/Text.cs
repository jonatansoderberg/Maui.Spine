namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Attached typography properties for <see cref="Label"/>: OpenType features such as tabular
/// digits, and a cap-height trim that centres capital letters on the label's box.
/// </summary>
/// <example>
/// <code>
/// &lt;Label Text="{Binding Time}" Text.FontFeatures="tnum" /&gt;
/// &lt;Label Text="{Binding Initials}" Text.TrimToCapHeight="True" /&gt;
/// </code>
/// </example>
public static class Text
{
    internal const string FontFeaturesMapperKey = "SpineFontFeatures";
    internal const string TrimToCapHeightMapperKey = "SpineTrimToCapHeight";

    /// <summary>
    /// Comma-separated OpenType feature tags applied to the label's font, e.g. <c>"tnum"</c> for
    /// equal-width digits or <c>"tnum,ss01"</c>. The label keeps its font family, size and weight.
    /// </summary>
    public static readonly BindableProperty FontFeaturesProperty = BindableProperty.CreateAttached(
        "FontFeatures", typeof(string), typeof(Text), null,
        propertyChanged: static (bindable, _, _) => (bindable as View)?.Handler?.UpdateValue(FontFeaturesMapperKey));

    /// <summary>Gets the OpenType feature tags applied to <paramref name="view"/>.</summary>
    public static string? GetFontFeatures(BindableObject view) => (string?)view.GetValue(FontFeaturesProperty);

    /// <summary>Sets the OpenType feature tags applied to <paramref name="view"/>.</summary>
    public static void SetFontFeatures(BindableObject view, string? value) => view.SetValue(FontFeaturesProperty, value);

    /// <summary>
    /// When <see langword="true"/>, shifts the glyphs so the capital letters sit on the centre line
    /// of the label's box instead of the line box, which puts initials in the middle of a pill or
    /// circle. A rendering translation only: layout is unchanged, and the label's own
    /// <see cref="VisualElement.TranslationY"/> is taken over.
    /// </summary>
    public static readonly BindableProperty TrimToCapHeightProperty = BindableProperty.CreateAttached(
        "TrimToCapHeight", typeof(bool), typeof(Text), false,
        propertyChanged: static (bindable, _, _) => (bindable as View)?.Handler?.UpdateValue(TrimToCapHeightMapperKey));

    /// <summary>Gets whether <paramref name="view"/> centres its capitals on its box.</summary>
    public static bool GetTrimToCapHeight(BindableObject view) => (bool)view.GetValue(TrimToCapHeightProperty);

    /// <summary>Sets whether <paramref name="view"/> centres its capitals on its box.</summary>
    public static void SetTrimToCapHeight(BindableObject view, bool value) => view.SetValue(TrimToCapHeightProperty, value);

    /// <summary>Splits the tag list into trimmed, lower-case, four-character tags.</summary>
    internal static IEnumerable<string> ParseTags(string? features) =>
        (features ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('\'', '"').ToLowerInvariant())
            .Where(t => t.Length == 4);

    /// <summary>
    /// The distance the glyphs sit below the box centre for a font whose line box runs from
    /// <paramref name="ascent"/> above the baseline to <paramref name="descent"/> below it, with
    /// capitals <paramref name="capHeight"/> tall. Positive means the capitals need to move up.
    /// </summary>
    internal static double CapOffset(double ascent, double descent, double capHeight) =>
        (ascent - capHeight - descent) / 2;
}
