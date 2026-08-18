using Microsoft.Maui.Controls.Shapes;

namespace Orientera.Controls;

/// <summary>
/// The picture at the top of a page: terrain of the kind the race is run in, never a claim about
/// the place itself (P7, beslut D2).
/// </summary>
/// <remarks>
/// The lookup is a rule, not a table: <c>terrain_&lt;discipline&gt;_&lt;terrain&gt;</c>, then
/// <c>terrain_&lt;discipline&gt;_default</c>, then whatever <see cref="Fallback"/> holds. The
/// naming rule and the images live in <c>Resources/Images/terrain/</c>.
/// <para>
/// The fallback is passed in rather than owned. The map tile is the one thing on the page that is
/// true geography, and it lives in <c>Features/Events/</c> — a control in <c>Controls/</c> that
/// reached for it would point the dependency the wrong way.
/// </para>
/// <para>
/// The bundled names are listed here because MAUI flattens image resources and offers no way to
/// ask whether one exists: a missing name renders as nothing at all, which is worse than falling
/// back. The list and the folder's README describe the same set.
/// </para>
/// </remarks>
public sealed class HeroImage : ContentView
{
    private static readonly HashSet<string> Bundled =
    [
        "sprint_urban", "sprint_default",
        "middle_skog", "middle_moran",
        "long_skog", "long_moran", "long_fjall",
        "ultralong_fjall",
        "night_skog",
        "relay_skog",
        "indoor_default",
    ];

    public static readonly BindableProperty DisciplineProperty =
        BindableProperty.Create(nameof(Discipline), typeof(string), typeof(HeroImage), string.Empty,
            propertyChanged: (b, _, _) => ((HeroImage)b).Apply());

    public static readonly BindableProperty TerrainProperty =
        BindableProperty.Create(nameof(Terrain), typeof(string), typeof(HeroImage), string.Empty,
            propertyChanged: (b, _, _) => ((HeroImage)b).Apply());

    public static readonly BindableProperty FallbackProperty =
        BindableProperty.Create(nameof(Fallback), typeof(View), typeof(HeroImage), null,
            propertyChanged: (b, _, _) => ((HeroImage)b).Apply());

    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly ContentView _fallbackSlot = new();
    private readonly Border _scrim = new()
    {
        StrokeThickness = 0,
        InputTransparent = true,
        VerticalOptions = LayoutOptions.Fill,
    };

    private readonly Grid _stack;

    public HeroImage()
    {
        // The gradient is what lets a badge sit on top of a photograph and still clear its contrast
        // requirement, so it belongs to the hero rather than to whoever puts something on it.
        var fade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };

        var clear = new GradientStop { Offset = 0.45f, Color = Colors.Transparent };
        var dark = new GradientStop { Offset = 1f };
        dark.SetDynamicResource(GradientStop.ColorProperty, "HeroScrim");

        fade.GradientStops.Add(clear);
        fade.GradientStops.Add(dark);
        _scrim.Background = fade;

        _stack = new Grid { Children = { _fallbackSlot, _image, _scrim } };

        AutomationProperties.SetIsInAccessibleTree(_image, false);
        AutomationProperties.SetIsInAccessibleTree(_scrim, false);

        HeightRequest = 220;
        Content = _stack;

        Apply();
    }

    /// <summary>The <c>Discipline</c> value in lower case: sprint, middle, long, …</summary>
    public string Discipline
    {
        get => (string)GetValue(DisciplineProperty);
        set => SetValue(DisciplineProperty, value);
    }

    /// <summary>urban, skog, moran, fjall, kust — empty when the terrain is not known.</summary>
    public string Terrain
    {
        get => (string)GetValue(TerrainProperty);
        set => SetValue(TerrainProperty, value);
    }

    /// <summary>Shown when no image matches. The map tile, on a competition page.</summary>
    public View? Fallback
    {
        get => (View?)GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    private void Apply()
    {
        _fallbackSlot.Content = Fallback;

        var name = Resolve();
        _image.Source = name is null ? null : ImageSource.FromFile($"terrain_{name}.jpg");
        _image.IsVisible = name is not null;

        // Nothing to darken, and a scrim over a map tile only makes the map harder to read.
        _scrim.IsVisible = name is not null;
    }

    private string? Resolve()
    {
        var discipline = Discipline?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(discipline))
            return null;

        var terrain = Terrain?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(terrain) && Bundled.Contains($"{discipline}_{terrain}"))
            return $"{discipline}_{terrain}";

        return Bundled.Contains($"{discipline}_default") ? $"{discipline}_default" : null;
    }
}
