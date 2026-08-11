using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using Orientera.Domain;
using MapsuiBrush = Mapsui.Styles.Brush;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiMap = Mapsui.Map;
using MapsuiPen = Mapsui.Styles.Pen;
using SymbolStyle = Mapsui.Styles.SymbolStyle;

namespace Orientera.Features.Events;

/// <summary>
/// The arena on a map. A competition is a place before it is anything else, and the page said
/// nothing about where it was.
/// </summary>
/// <remarks>
/// The background is OpenStreetMap, ours to show as long as it is credited. Mapsui draws the
/// tile source's own attribution, which is why nothing here repeats it — a credit that follows
/// the layer cannot fall out of step with it.
///
/// The orienteering map itself belongs to the club that drew it and is shared per map through
/// Omaps, to the external services the owner names. When that access exists it becomes another
/// layer above this one, and its credit arrives with it (SP-05).
/// </remarks>
public sealed class ArenaMap : MapControl
{
    /// <summary>Metres per pixel — close enough to see the roads in, far enough to place it.</summary>
    private const double ArenaResolution = 4.0;

    public static readonly BindableProperty ArenaProperty = BindableProperty.Create(
        nameof(Arena),
        typeof(GeoPoint),
        typeof(ArenaMap),
        default(GeoPoint),
        propertyChanged: (bindable, _, _) => ((ArenaMap)bindable).Rebuild());

    public GeoPoint Arena
    {
        get => (GeoPoint)GetValue(ArenaProperty);
        set => SetValue(ArenaProperty, value);
    }

    private void Rebuild()
    {
        // A competition without a position is a competition we cannot draw.
        if (Arena is { Latitude: 0, Longitude: 0 })
            return;

        var (x, y) = SphericalMercator.FromLonLat(Arena.Longitude, Arena.Latitude);
        var centre = new MPoint(x, y);

        var map = new MapsuiMap { CRS = "EPSG:3857" };
        map.Layers.Add(OpenStreetMap.CreateTileLayer("Orientera"));
        map.Layers.Add(ArenaLayer(centre));
        Map = map;

        // Mapsui 5 navigates through the map's own navigator; there is no Home hook.
        Map.Navigator.CenterOnAndZoomTo(centre, ArenaResolution);
    }

    private static MemoryLayer ArenaLayer(MPoint centre) => new("Arena")
    {
        Features = [new PointFeature(centre)],
        Style = new SymbolStyle
        {
            SymbolScale = 0.9,
            Fill = new MapsuiBrush(MapsuiColor.FromString("#E8590C")),
            Outline = new MapsuiPen(MapsuiColor.White, 2),
        },
    };
}
