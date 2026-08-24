using System.Numerics;

namespace Orientera.Backend.Arena;

/// <summary>
/// Överlagringarna i bildplanet: muren längs tävlingsområdets gräns, gränslinjen, arenaljuset.
/// </summary>
/// <remarks>
/// Allt här ritas mot djupbufferten från renderingen, så att en ås som ligger framför gränsen
/// också skymmer den. Muren ritas som en volym i stället för ett streck: den har överkant,
/// sidoyta och skuggsida, vilket är sådant en bildmodell återger bra — ett hårstreck är
/// däremot det första diffusion smetar ut.
/// </remarks>
public static class Overlays
{
    /// <summary>Hur långt bakom djupbufferten en punkt får ligga och ändå räknas som synlig.</summary>
    private const double DepthSlack = 40.0;

    /// <summary>Var arenan hamnar i bild, och om terrängen låter den synas. <c>null</c> annars.</summary>
    public static (double X, double Y, double Distance)? ArenaOnScreen(
        (double X, double Y) arena, ScalarGrid elevation, SwerefBounds bounds, RenderResult render)
    {
        var z = GroundHeight(arena.X, arena.Y, elevation, bounds);
        if (render.Project(arena.X, arena.Y, z) is not { } p)
            return null;
        var (width, height) = (render.Image.Width, render.Image.Height);
        if (p.X < 0 || p.X >= width || p.Y < 0 || p.Y >= height)
            return null;
        return p.Distance > render.Depth[(int)p.X, (int)p.Y] + DepthSlack ? null : p;
    }

    /// <summary>Vimpelns höjd i bildpixlar: större nära kameran, mindre långt bort, inom rimliga hak.</summary>
    public static double FlagHeight(double distance, RenderResult render, int imageHeight) =>
        imageHeight * 0.132 * Math.Clamp(render.MidDistance / distance, 0.75, 1.4);

    /// <summary>
    /// Murens synliga kvadrar i bildplanet, sorterade bak-till-fram. Delas mellan ritningen
    /// och murkontrollen — kontrollen mäter på exakt de ytor som ritades.
    /// </summary>
    public static IReadOnlyList<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)> WallQuads(
        IReadOnlyList<(double X, double Y)> area, ScalarGrid elevation,
        SwerefBounds bounds, RenderResult render,
        double heightMeters = 14.0, double stepMeters = 5.0)
    {
        var width = render.Image.Width;
        var height = render.Image.Height;
        var samples = new List<((double X, double Y, double Distance) Ground, (double X, double Y, double Distance) Top)?>();

        foreach (var (east, north) in RingSamples(area, stepMeters))
        {
            var z = GroundHeight(east, north, elevation, bounds);
            var ground = render.Project(east, north, z);
            var top = render.Project(east, north, z + heightMeters);
            if (ground is not { } g || top is not { } t)
            {
                samples.Add(null);
                continue;
            }
            var visible = g.X >= 0 && g.X < width && g.Y > -height && g.Y < 2 * height
                && g.Distance <= render.Depth[
                    (int)Math.Clamp(g.X, 0, width - 1), (int)Math.Clamp(g.Y, 0, height - 1)] + DepthSlack;
            samples.Add(visible ? (g, t) : null);
        }

        var quads = new List<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)>();
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] is not { } a || samples[(i + 1) % samples.Count] is not { } b)
                continue;
            quads.Add((Math.Max(a.Ground.Distance, b.Ground.Distance),
                [(a.Top.X, a.Top.Y), (b.Top.X, b.Top.Y), (b.Ground.X, b.Ground.Y), (a.Ground.X, a.Ground.Y)],
                [(a.Top.X, a.Top.Y), (b.Top.X, b.Top.Y)]));
        }
        quads.Sort((left, right) => right.Distance.CompareTo(left.Distance));
        return quads;
    }

    /// <summary>
    /// Tävlingsområdets gräns som en mur i markplanet — halvgenomskinlig med flit, så den
    /// visar var gränsen går utan att dölja terrängen bakom.
    /// </summary>
    /// <remarks>
    /// Muren ritas på ett eget lager som alfakomponeras en gång: målades kvadrarna direkt
    /// mot bilden skulle överlappen i perspektivet dubbelblandas och ge fläckvis olika
    /// täckning. I mörker ska muren lysa: halon läggs additivt så marken under syns igenom,
    /// och den skarpa muren ritas ovanpå — annars blir den suddig i stället för lysande.
    /// </remarks>
    public static void DrawWall(
        ColorGrid image,
        IReadOnlyList<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)> quads,
        bool glow = false)
    {
        var width = image.Width;
        var height = image.Height;

        void Paint(Rgba fill, Rgba cap, int capWidth)
        {
            var layer = new RgbaLayer(width, height);
            foreach (var (_, quad, top) in quads)
            {
                Rasterizer.FillPolygon(layer.Color, quad, fill with { A = 255 });
                Rasterizer.FillPolygon(layer.Alpha, quad, fill.A / 255f);
                Rasterizer.DrawLine(layer.Color, top[0], top[1], capWidth, cap with { A = 255 });
                Rasterizer.DrawLine(layer.Alpha, top[0], top[1], capWidth, cap.A / 255f);
            }
            layer.CompositeOver(image, 0, 0);
        }

        if (glow)
        {
            var halo = new ColorGrid(width, height);
            var alpha = new ScalarGrid(width, height);
            foreach (var (_, quad, top) in quads)
            {
                Rasterizer.FillPolygon(halo, quad, new Rgba(220, 96, 18));
                Rasterizer.FillPolygon(alpha, quad, 1f);
                Rasterizer.DrawLine(halo, top[0], top[1], 3, new Rgba(240, 150, 70));
                Rasterizer.DrawLine(alpha, top[0], top[1], 3, 1f);
            }
            var blurredHalo = SmoothChannels(halo, 14);
            var blurredAlpha = GridMath.Smooth(alpha, 14);
            for (var p = 0; p < alpha.Values.Length; p++)
            {
                var i = p * 3;
                var a = blurredAlpha.Values[p] * 0.85f;
                image.Values[i] = Math.Clamp(image.Values[i] + blurredHalo.Values[i] * a, 0f, 1f);
                image.Values[i + 1] = Math.Clamp(image.Values[i + 1] + blurredHalo.Values[i + 1] * a, 0f, 1f);
                image.Values[i + 2] = Math.Clamp(image.Values[i + 2] + blurredHalo.Values[i + 2] * a, 0f, 1f);
            }
            Paint(new Rgba(186, 84, 22, 128), new Rgba(226, 140, 70, 128), 2);
        }
        else
        {
            Paint(new Rgba(236, 104, 12, 128), new Rgba(255, 168, 74, 128), 3);
        }
    }

    /// <summary>
    /// Tävlingsområdets gräns i bildplanet, med ockluderingstest mot djupbufferten.
    /// Ett segment ritas bara när båda ändarna syns — annars skulle gränsen löpa tvärs
    /// igenom en ås som ligger framför den.
    /// </summary>
    public static void DrawOutline(
        ColorGrid image, IReadOnlyList<(double X, double Y)> area, ScalarGrid elevation,
        SwerefBounds bounds, RenderResult render, double stepMeters = 6.0)
    {
        var width = image.Width;
        var height = image.Height;
        var points = new List<(double X, double Y)?>();
        foreach (var (east, north) in RingSamples(area, stepMeters))
        {
            var z = GroundHeight(east, north, elevation, bounds);
            if (render.Project(east, north, z) is not { } p
                || p.X < 0 || p.X >= width || p.Y < 0 || p.Y >= height
                || p.Distance > render.Depth[(int)p.X, (int)p.Y] + DepthSlack)
            {
                points.Add(null);
                continue;
            }
            points.Add((p.X, p.Y));
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is { } a && points[(i + 1) % points.Count] is { } b)
                Rasterizer.DrawLine(image, a, b, 4, new Rgba(255, 108, 0, 240));
        }
    }

    /// <summary>
    /// Vimpeln som ljuskälla i mörker. Additivt, inte alfa över: ljus lägger sig till det som
    /// redan finns, så marken under fortsätter synas igenom skenet. Faller av kvadratiskt och
    /// plattas ut mot marken, eftersom ljuskällan står strax ovanför den.
    /// </summary>
    public static void NightGlow(ColorGrid image, (double X, double Y) at, double radius, float strength = 0.42f)
    {
        var warm = new Vector3(1.00f, 0.68f, 0.34f);
        Parallel.For(0, image.Height, y =>
        {
            for (var x = 0; x < image.Width; x++)
            {
                var dx = (x - at.X) / radius;
                var dy = (y - at.Y) / (radius * 0.42);
                var falloff = MathF.Pow((float)Math.Clamp(1.0 - Math.Sqrt(dx * dx + dy * dy), 0, 1), 2.2f);
                if (falloff <= 0)
                    continue;
                var i = image.IndexOf(x, y);
                image.Values[i] = Math.Clamp(image.Values[i] + falloff * warm.X * strength, 0f, 1f);
                image.Values[i + 1] = Math.Clamp(image.Values[i + 1] + falloff * warm.Y * strength, 0f, 1f);
                image.Values[i + 2] = Math.Clamp(image.Values[i + 2] + falloff * warm.Z * strength, 0f, 1f);
            }
        });
    }

    /// <summary>
    /// Bara markskenet, utan vimpel. Går bilden till en bildmodell måste ljuskällan finnas i
    /// indata — modellen kan inte veta att det kommer stå en upplyst vimpel där, och skulle
    /// rendera marken beckmörk. Flaggan själv utelämnas: bokstäver är det diffusion är sämst på.
    /// </summary>
    public static void PlaceGlow(ColorGrid image, (double X, double Y, double Distance) position, RenderResult render)
    {
        var flagHeight = FlagHeight(position.Distance, render, image.Height);
        NightGlow(image, (position.X, position.Y - flagHeight * 0.22), flagHeight * 1.7);
    }

    /// <summary>Ringen tätad till punkter med jämna mellanrum, i samma ordning som prototypen.</summary>
    private static IEnumerable<(double X, double Y)> RingSamples(
        IReadOnlyList<(double X, double Y)> area, double stepMeters)
    {
        for (var i = 0; i < area.Count; i++)
        {
            var (x0, y0) = area[i];
            var (x1, y1) = area[(i + 1) % area.Count];
            var segments = Math.Max(2, (int)(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / stepMeters));
            for (var j = 0; j < segments; j++)
                yield return (x0 + (x1 - x0) * j / (double)segments, y0 + (y1 - y0) * j / (double)segments);
        }
    }

    private static float GroundHeight(double east, double north, ScalarGrid elevation, SwerefBounds bounds) =>
        elevation.Sample(
            (east - bounds.MinX) / bounds.Width * (elevation.Width - 1),
            (bounds.MaxY - north) / bounds.Height * (elevation.Height - 1));

    private static ColorGrid SmoothChannels(ColorGrid layer, double sigma)
    {
        var result = new ColorGrid(layer.Width, layer.Height);
        for (var channel = 0; channel < 3; channel++)
        {
            var plane = new ScalarGrid(layer.Width, layer.Height);
            for (var p = 0; p < plane.Values.Length; p++)
                plane.Values[p] = layer.Values[p * 3 + channel];
            var blurred = GridMath.Smooth(plane, sigma);
            for (var p = 0; p < plane.Values.Length; p++)
                result.Values[p * 3 + channel] = blurred.Values[p];
        }
        return result;
    }
}
