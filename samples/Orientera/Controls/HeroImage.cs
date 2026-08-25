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
        "middle_skog", "middle_moran", "middle_default",
        "long_skog", "long_moran", "long_fjall", "long_default",
        "ultralong_fjall", "ultralong_default",
        "night_skog", "night_default",
        "relay_skog", "relay_default",
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

    public static readonly BindableProperty ArenaSourceProperty =
        BindableProperty.Create(nameof(ArenaSource), typeof(string), typeof(HeroImage), null,
            propertyChanged: (b, _, _) => ((HeroImage)b).Apply());

    public static readonly BindableProperty AttributionProperty =
        BindableProperty.Create(nameof(Attribution), typeof(string), typeof(HeroImage), string.Empty,
            propertyChanged: (b, _, _) => ((HeroImage)b).Apply());

    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly ContentView _fallbackSlot = new();
    private readonly Grid _stack;

    /// <summary>
    /// CC BY 4.0 kräver att krediteringen står bredvid bilden, och bilden bär ingen text
    /// själv — så raden bor i hjälten. Diskret: liten, vit med skuggkant för läsbarheten,
    /// i stället för en gradient som skulle mörka ned själva bilden.
    /// </summary>
    private readonly Label _attribution = new()
    {
        FontSize = 8,
        TextColor = Colors.White,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.End,
        Margin = new Thickness(12, 0, 12, 5),
        LineBreakMode = LineBreakMode.TailTruncation,
        Shadow = new Shadow
        {
            Brush = new SolidColorBrush(Colors.Black),
            Radius = 3,
            Opacity = 0.7f,
            Offset = new Point(0, 1),
        },
    };

    // Ingen skymningsgradient över bilden: den fanns för märken ovanpå fotot, men inget
    // ligger där längre, och den mörkade ned varje hjälte i onödan. Krediteringen bär sin
    // egen skugga i stället.
    public HeroImage()
    {
        _stack = new Grid { Children = { _fallbackSlot, _image, _attribution } };

        AutomationProperties.SetIsInAccessibleTree(_image, false);

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

    /// <summary>
    /// Url till tävlingens egen arenabild, när den hunnit genereras. Tom betyder att den
    /// medföljande terrängbilden står kvar som platshållare — den cachas inte, för nästa
    /// besök kan ha en riktig bild att visa. Bloburlen bär renderarens version i sökvägen,
    /// så innehållet bakom en given url ändras aldrig och får cachas länge på enheten.
    /// </summary>
    public string? ArenaSource
    {
        get => (string?)GetValue(ArenaSourceProperty);
        set => SetValue(ArenaSourceProperty, value);
    }

    /// <summary>Krediteringen som måste följa arenabilden. Tom när ingen arenabild visas.</summary>
    public string Attribution
    {
        get => (string)GetValue(AttributionProperty);
        set => SetValue(AttributionProperty, value);
    }

    private void Apply()
    {
        _fallbackSlot.Content = Fallback;

        var arena = ArenaSource;
        var name = Resolve();

        if (!string.IsNullOrEmpty(arena) && Uri.TryCreate(arena, UriKind.Absolute, out var uri))
        {
            _image.Source = new UriImageSource
            {
                Uri = uri,
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromDays(180),
            };
        }
        else
        {
            arena = null;
            _image.Source = name is null ? null : ImageSource.FromFile($"terrain_{name}.jpg");
        }
        _image.IsVisible = arena is not null || name is not null;

        _attribution.Text = Attribution;
        _attribution.IsVisible = arena is not null && !string.IsNullOrEmpty(Attribution);

        // Hidden rather than merely covered: a fallback left alive behind the picture is a second
        // map fetching its own tiles for a view nobody sees.
        _fallbackSlot.IsVisible = !_image.IsVisible && Fallback is not null;

        // Neither a picture nor a fallback is a blank band across the top of the page.
        IsVisible = _image.IsVisible || Fallback is not null;
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
