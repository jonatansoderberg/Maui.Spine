using Microsoft.Extensions.Logging;
using Orientera.Services.Sources;

namespace Orientera.Backend.Arena;

/// <summary>
/// Det som ska renderas: tävlingsområdets hörn och arenan i SWEREF99 TM, och om hörnen är
/// arrangörens egna eller bara en ram kring arenan.
/// </summary>
/// <remarks>
/// Saknas området i Eventor ritas ingen gräns — att gissa fram en och rita den vore att
/// hitta på arrangörens gränsdragning. Ramen finns ändå, för kameran måste rikta sig mot något.
/// </remarks>
public sealed record ArenaGeometry(
    IReadOnlyList<(double X, double Y)> Area,
    (double X, double Y) Arena,
    bool HasOutline)
{
    /// <summary>
    /// Rutan som ramar in en tävling utan polygon. Storleken är en gissning på en
    /// medeldistans; arenan antas ligga mitt i, vilket den sällan gör.
    /// </summary>
    public const double DefaultSideMeters = 1300.0;

    public static ArenaGeometry From(
        (double Latitude, double Longitude) arena,
        IReadOnlyList<(double Latitude, double Longitude)>? area)
    {
        var arenaXy = SwedishProjection.ToSweref(arena.Latitude, arena.Longitude);
        if (area is { Count: >= 3 })
            return new ArenaGeometry(
                [.. area.Select(p => SwedishProjection.ToSweref(p.Latitude, p.Longitude))],
                arenaXy, HasOutline: true);

        var half = DefaultSideMeters / 2;
        return new ArenaGeometry(
        [
            (arenaXy.East - half, arenaXy.North - half),
            (arenaXy.East + half, arenaXy.North - half),
            (arenaXy.East + half, arenaXy.North + half),
            (arenaXy.East - half, arenaXy.North + half),
        ], arenaXy, HasOutline: false);
    }
}

/// <summary>
/// Den nakna renderingen och allt överlagringarna behöver för att läggas på efter
/// bildmodellens pass: djupbufferten, projektionen och arenans läge i bild.
/// </summary>
public sealed class ArenaBareScene
{
    public required ColorGrid Image { get; init; }
    public required RenderResult Render { get; init; }
    public required ScalarGrid Elevation { get; init; }
    public required SwerefBounds Bounds { get; init; }
    public required ArenaGeometry Geometry { get; init; }
    public required Lighting Light { get; init; }
    public required (double X, double Y, double Distance)? ArenaOnScreen { get; init; }

    /// <summary>Murens ytor i bildplanet, för murkontrollen. <c>null</c> när ingen gräns finns.</summary>
    public required IReadOnlyList<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)>? WallQuads { get; init; }
}

/// <summary>
/// Komponerar den nakna tävlingsbilden: terräng, ljus, mur och arenaljus — allt utom det
/// bildmodellen och de efterföljande överlagringarna står för.
/// </summary>
/// <remarks>
/// Bilden är avsiktligt naken: gräns ritas som mur i markplanet (en volym som diffusion
/// återger i stället för smetar ut), men vimpel och text hör hemma efter bildmodellens pass.
/// </remarks>
public sealed class ArenaComposer(
    TerrainSource _terrain,
    LantmaterietClient _client,
    ILogger<ArenaComposer> _logger)
{
    /// <summary>
    /// gpt-image-2 tar valfri upplösning, men sidorna måste vara multiplar av 16. 1920x1080
    /// är det inte; 1088 är närmaste giltiga och ger 16:9 med 0,7 procents fel.
    /// </summary>
    public const int Width = 1920;
    public const int Height = 1088;

    public async Task<ArenaBareScene?> ComposeBareAsync(
        ArenaGeometry geometry, ArenaSeason season, Lighting light, CancellationToken cancellationToken)
    {
        var bounds = TerrainRenderer.FrameBounds(
            geometry.Area, CameraSettings.Default, Width, Height, fitArea: geometry.HasOutline);
        var gridWidth = (int)(bounds.Width / TerrainRenderer.GroundResolution);
        var gridHeight = (int)(bounds.Height / TerrainRenderer.GroundResolution);

        var elevation = await _terrain.ElevationAsync(bounds, gridWidth, gridHeight, cancellationToken);
        if (elevation is null)
        {
            _logger.LogWarning("Höjdmodellen gick inte att hämta — ingen bild utan den.");
            return null;
        }
        var orthophoto = await _client.OrthophotoAsync(bounds, gridWidth, gridHeight, cancellationToken);

        return ComposeBare(geometry, season, light, bounds, elevation, orthophoto, Width, Height);
    }

    /// <summary>Den rena kompositionen, skild från hämtningen så den kan mätas mot facit utan nät.</summary>
    public static ArenaBareScene ComposeBare(
        ArenaGeometry geometry, ArenaSeason season, Lighting light,
        SwerefBounds bounds, ScalarGrid elevation, ColorGrid orthophoto, int width, int height)
    {
        var seasonLook = SeasonLook.All[season];

        // Mikroformen skuggas med kartografisk konvention (nordväst, 38°) oavsett var solen
        // står — det är detaljteckning, inte ljussättning. Solens riktning styr i stället
        // den lambertska skuggningen inne i texturbygget.
        var shade = TerrainTexture.Hillshade(elevation, TerrainRenderer.GroundResolution,
            315.0 * Math.PI / 180.0, 38.0 * Math.PI / 180.0);
        var texture = TerrainTexture.ShadeTexture(
            orthophoto, shade, elevation, TerrainRenderer.GroundResolution, seasonLook, light);

        var render = TerrainRenderer.Render(
            bounds, elevation, texture, geometry.Area, light, width, height,
            CameraSettings.Default, vexMax: 1.35, fitArea: geometry.HasOutline);
        var image = ImageGrade.Apply(render.Image, light.Grade ?? seasonLook.Grade);

        var arenaOnScreen = Overlays.ArenaOnScreen(geometry.Arena, elevation, bounds, render);
        var wallQuads = geometry.HasOutline
            ? Overlays.WallQuads(geometry.Area, elevation, bounds, render)
            : null;
        if (wallQuads is not null)
            Overlays.DrawWall(image, wallQuads, glow: light.Night);
        if (arenaOnScreen is { } position && light.Night)
            Overlays.PlaceGlow(image, position, render);

        return new ArenaBareScene
        {
            Image = image,
            Render = render,
            Elevation = elevation,
            Bounds = bounds,
            Geometry = geometry,
            Light = light,
            ArenaOnScreen = arenaOnScreen,
            WallQuads = wallQuads,
        };
    }

    /// <summary>
    /// Överlagringarna ovanpå bildmodellens resultat, i bildplanet: vimpeln på arenan, med
    /// nattsken när tävlingen springs i mörker. Muren står redan i bilden — den följde med
    /// genom modellen — så ingen gräns ritas om.
    /// </summary>
    public static ColorGrid ApplyOverlays(ArenaBareScene scene, ColorGrid enhanced)
    {
        if (scene.ArenaOnScreen is not { } position)
            return enhanced;
        if (scene.Light.Night)
        {
            var flagHeight = Overlays.FlagHeight(position.Distance, scene.Render, enhanced.Height);
            Overlays.NightGlow(enhanced, (position.X, position.Y - flagHeight * 0.22), flagHeight * 1.7);
        }
        Flag.Draw(enhanced, (position.X, position.Y),
            Overlays.FlagHeight(position.Distance, scene.Render, enhanced.Height));
        return enhanced;
    }
}
