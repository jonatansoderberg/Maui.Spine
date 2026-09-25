namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Attached typography properties for <see cref="Label"/>: OpenType features such as tabular
/// digits, and a cap-height trim that centres capital letters on the label's box.
/// </summary>
/// <example>
/// <code>
/// &lt;Label Text="{Binding Time}" Text.FontFeatures="TabularFigures" /&gt;
/// &lt;Label Text="{Binding Initials}" Text.TrimToCapHeight="True" /&gt;
/// </code>
/// </example>
public static class Text
{
    internal const string FontFeaturesMapperKey = "SpineFontFeatures";
    internal const string TrimToCapHeightMapperKey = "SpineTrimToCapHeight";

    /// <summary>
    /// Comma-separated OpenType features applied to the label's font, by name or by tag:
    /// <c>"TabularFigures"</c> for equal-width digits, <c>"TabularFigures, StylisticSet1"</c>, or a
    /// four-letter tag such as <c>"tnum"</c> or <c>"ss07"</c> for a feature without a name.
    /// The label keeps its font family, size and weight.
    /// </summary>
    public static readonly BindableProperty FontFeaturesProperty = BindableProperty.CreateAttached(
        "FontFeatures", typeof(string), typeof(Text), null,
        propertyChanged: static (bindable, _, _) => (bindable as View)?.Handler?.UpdateValue(FontFeaturesMapperKey));

    /// <summary>Gets the OpenType features applied to <paramref name="view"/>.</summary>
    public static string? GetFontFeatures(BindableObject view) => (string?)view.GetValue(FontFeaturesProperty);

    /// <summary>Sets the OpenType features applied to <paramref name="view"/>.</summary>
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

    static readonly Dictionary<string, string[]> FeatureNames = CreateFeatureNames();

    static Dictionary<string, string[]> CreateFeatureNames()
    {
        var names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["TabularFigures"] = ["tnum"],
            ["ProportionalFigures"] = ["pnum"],
            ["LiningFigures"] = ["lnum"],
            ["OldstyleFigures"] = ["onum"],
            ["SlashedZero"] = ["zero"],
            ["Fractions"] = ["frac"],
            ["Superscript"] = ["sups"],
            ["Subscript"] = ["subs"],
            ["Ordinals"] = ["ordn"],
            ["SmallCaps"] = ["smcp"],
            ["AllSmallCaps"] = ["smcp", "c2sc"],
            ["CaseSensitiveForms"] = ["case"],
            ["Kerning"] = ["kern"],
            ["StandardLigatures"] = ["liga"],
            ["DiscretionaryLigatures"] = ["dlig"],
            ["ContextualAlternates"] = ["calt"],
        };

        for (var i = 1; i <= 20; i++)
            names[$"StylisticSet{i}"] = [$"ss{i:00}"];

        return names;
    }

    /// <summary>
    /// Splits the feature list into lower-case, four-character OpenType tags; a feature name
    /// becomes its tags, anything else that is four characters long is taken as a tag.
    /// </summary>
    internal static IEnumerable<string> ParseTags(string? features) =>
        (features ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('\'', '"'))
            .SelectMany(t => FeatureNames.TryGetValue(t, out var tags) ? tags : [t.ToLowerInvariant()])
            .Where(t => t.Length == 4)
            .Distinct();

    /// <summary>
    /// The distance the glyphs sit below the box centre for a font whose line box runs from
    /// <paramref name="ascent"/> above the baseline to <paramref name="descent"/> below it, with
    /// capitals <paramref name="capHeight"/> tall. Positive means the capitals need to move up.
    /// </summary>
    internal static double CapOffset(double ascent, double descent, double capHeight) =>
        (ascent - capHeight - descent) / 2;
}
