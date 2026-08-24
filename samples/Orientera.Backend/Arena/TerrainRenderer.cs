using System.Numerics;

namespace Orientera.Backend.Arena;

/// <summary>Kamerans geometri. Vinkeln i radianer, resten i planens egna mått.</summary>
/// <param name="Azimuth">Blickriktning i radianer medurs från norr.</param>
/// <param name="Pitch">Depressionsvinkel i grader mot områdets mitt.</param>
/// <param name="Fill">Hur stor del av bildhöjden tävlingsområdet ska uppta.</param>
/// <param name="Reach">Snedavståndet till områdets mitt, i multiplar av områdets utsträckning.</param>
/// <param name="Back">Hur långt bortom området strålarna marscherar, i områdeslängder.</param>
/// <param name="CenterY">Var i bildhöjden områdets mitt hamnar.</param>
public sealed record CameraSettings(
    double Azimuth, double Pitch, double Fill, double Reach, double Back, double CenterY)
{
    /// <summary>Prototypens kamera: sydsydväst-vy med området två tredjedelar upp i bild.</summary>
    public static readonly CameraSettings Default = new(200 * Math.PI / 180.0, 21, 0.66, 2.3, 1.2, 0.42);
}

/// <summary>Projicerar en världspunkt till (kolumn, rad, avstånd). Avståndet jämförs mot djupbufferten.</summary>
public delegate (double X, double Y, double Distance)? WorldProjector(double east, double north, double groundZ);

/// <summary>Den färdiga snedbilden med det som överlagringarna behöver för att stå i den.</summary>
public sealed class RenderResult
{
    public required ColorGrid Image { get; init; }

    /// <summary>Djup i meter per pixel; oändligt där himlen syns.</summary>
    public required ScalarGrid Depth { get; init; }

    public required WorldProjector Project { get; init; }
    public required double Vex { get; init; }
    public required double Relief { get; init; }
    public required double CameraHeight { get; init; }
    public required double MidDistance { get; init; }
}

/// <summary>
/// Renderar en snedbild över ett höjdfält.
/// </summary>
/// <remarks>
/// En klassisk voxel-strålmarsch: vyn samplas som ett rutnät i kamerans eget koordinatsystem
/// (djup gånger sidled), varje kolumn projiceras, och ockludering faller ut gratis ur en
/// suffixminimering. Kameran ställs geometriskt: depressionsvinkeln och fyllnadsgraden ger
/// brännvidden, och huvudpunkten skjuts uppåt ur bild — ett tilt-shift, vilket är riktig
/// perspektiv och håller horisonten utanför ramen.
/// </remarks>
public static class TerrainRenderer
{
    /// <summary>Marktexlarnas storlek i meter. Konturen trappar synligt vid grövre upplösning.</summary>
    public const double GroundResolution = 1.25;

    private const int Steps = 1500;

    /// <summary>Tävlingsområdets mittpunkt och utsträckning längs och tvärs blickriktningen.</summary>
    public static ((double X, double Y) Center, double Along, double Across) ViewExtent(
        IReadOnlyList<(double X, double Y)> area, double azimuth)
    {
        var fx = Math.Sin(azimuth);
        var fy = Math.Cos(azimuth);
        var rx = Math.Cos(azimuth);
        var ry = -Math.Sin(azimuth);
        double alongMin = double.MaxValue, alongMax = double.MinValue;
        double acrossMin = double.MaxValue, acrossMax = double.MinValue;
        double sumX = 0, sumY = 0;
        foreach (var (x, y) in area)
        {
            var along = x * fx + y * fy;
            var across = x * rx + y * ry;
            alongMin = Math.Min(alongMin, along);
            alongMax = Math.Max(alongMax, along);
            acrossMin = Math.Min(acrossMin, across);
            acrossMax = Math.Max(acrossMax, across);
            sumX += x;
            sumY += y;
        }
        return ((sumX / area.Count, sumY / area.Count), alongMax - alongMin, acrossMax - acrossMin);
    }

    /// <summary>Marktäckningen kamerans frustum faktiskt behöver — inte en ruta runt området.</summary>
    /// <summary>
    /// Brännvidden ur fyllnadsgraden — och med <paramref name="fitArea"/> krympt tills varje
    /// hörn av området ryms i bild med marginal.
    /// </summary>
    /// <remarks>
    /// Fyllnadsformeln ser bara utsträckningen längs blickriktningen; ett brett eller
    /// framskjutet område kan ändå sticka ut ur ramen i sidled eller nedtill. När området är
    /// arrangörens eget — muren är hela poängen — provas därför varje hörn mot tre
    /// begränsningar: sidled, nederkanten (nära hörn hamnar lågt) och överkanten, där murens
    /// topp med höjdöverdrift får plats med. Höjderna räknas på plan mark, för brännvidden
    /// måste väljas innan höjdmodellen är hämtad — och den styr hur mycket mark som hämtas.
    /// </remarks>
    private static double Focal(
        IReadOnlyList<(double X, double Y)> area, CameraSettings camera,
        int width, int height, bool fitArea)
    {
        var pitch = camera.Pitch * Math.PI / 180.0;
        var tan = Math.Tan(pitch);
        var ((centerX, centerY), along, across) = ViewExtent(area, camera.Azimuth);
        var midDistance = camera.Reach * Math.Max(along, across) * Math.Cos(pitch);
        var focal = camera.Fill * height * midDistance / (along * Math.Sin(pitch));
        if (!fitArea)
            return focal;

        var fx = Math.Sin(camera.Azimuth);
        var fy = Math.Cos(camera.Azimuth);
        var rx = Math.Cos(camera.Azimuth);
        var ry = -Math.Sin(camera.Azimuth);
        var camX = centerX - fx * midDistance;
        var camY = centerY - fy * midDistance;
        var cameraHeight = midDistance * tan;

        // Murens topp inklusive största höjdöverdrift: 14 m gånger 1,35, plus lite duk.
        const double wallAllowance = 20.0 * 1.35;

        foreach (var (x, y) in area)
        {
            var distance = Math.Max(60.0, (x - camX) * fx + (y - camY) * fy);
            var offset = Math.Abs((x - camX) * rx + (y - camY) * ry);

            if (offset > 1)
                focal = Math.Min(focal, 0.46 * width * distance / offset);

            var drop = cameraHeight / distance - tan;
            if (drop > 1e-9)
                focal = Math.Min(focal, (1 - camera.CenterY - 0.05) * height / drop);

            var rise = tan - cameraHeight / distance + wallAllowance / distance;
            if (rise > 1e-9)
                focal = Math.Min(focal, (camera.CenterY - 0.06) * height / rise);
        }
        return focal;
    }

    /// <summary>
    /// Närmaste avstånd strålmarschen samplar. Med områdespassning kan vyn bli så vid att
    /// marken närmast kameran annars hamnar under närmaste djupsteg — då smetas bildens
    /// nederkant ut i kolumnränder — så gränsen dras in till frustumets nederkant.
    /// </summary>
    private static double NearDistance(
        CameraSettings camera, double midDistance, double along, double focal, int height, bool fitArea)
    {
        var nearDistance = Math.Max(60.0, midDistance - along * 0.85);
        if (!fitArea)
            return nearDistance;

        var tan = Math.Tan(camera.Pitch * Math.PI / 180.0);
        var bottomDistance = midDistance * tan / (tan + height * (1 - camera.CenterY) / focal);
        return Math.Min(nearDistance, Math.Max(20.0, bottomDistance * 0.85));
    }

    public static SwerefBounds FrameBounds(
        IReadOnlyList<(double X, double Y)> area, CameraSettings camera, int width, int height,
        bool fitArea = false)
    {
        var pitch = camera.Pitch * Math.PI / 180.0;
        var ((centerX, centerY), along, across) = ViewExtent(area, camera.Azimuth);
        var midDistance = camera.Reach * Math.Max(along, across) * Math.Cos(pitch);
        var focal = Focal(area, camera, width, height, fitArea);
        var fx = Math.Sin(camera.Azimuth);
        var fy = Math.Cos(camera.Azimuth);
        var rx = Math.Cos(camera.Azimuth);
        var ry = -Math.Sin(camera.Azimuth);
        var camX = centerX - fx * midDistance;
        var camY = centerY - fy * midDistance;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var distance in new[]
        {
            NearDistance(camera, midDistance, along, focal, height, fitArea),
            midDistance + along * camera.Back,
        })
        {
            var half = distance * (width / 2.0) / focal + 80;
            var x = camX + fx * distance;
            var y = camY + fy * distance;
            foreach (var sign in new[] { -1, 1 })
            {
                minX = Math.Min(minX, x + rx * half * sign);
                maxX = Math.Max(maxX, x + rx * half * sign);
                minY = Math.Min(minY, y + ry * half * sign);
                maxY = Math.Max(maxY, y + ry * half * sign);
            }
        }
        return new SwerefBounds(minX, minY, maxX, maxY);
    }

    public static RenderResult Render(
        SwerefBounds bounds, ScalarGrid elevation, ColorGrid texture,
        IReadOnlyList<(double X, double Y)> area, Lighting light,
        int width, int height, CameraSettings camera, double vexMax = 1.7,
        bool fitArea = false)
    {
        var pitch = camera.Pitch * Math.PI / 180.0;
        var fx = Math.Sin(camera.Azimuth);
        var fy = Math.Cos(camera.Azimuth);
        var rx = Math.Cos(camera.Azimuth);
        var ry = -Math.Sin(camera.Azimuth);
        var gridWidth = elevation.Width;
        var gridHeight = elevation.Height;

        var ((areaX, areaY), along, across) = ViewExtent(area, camera.Azimuth);
        var baseLevel = GridMath.Percentile(elevation.Values, 30);
        var relief = GridMath.Percentile(elevation.Values, 98) - GridMath.Percentile(elevation.Values, 2);

        var reach = camera.Reach * Math.Max(along, across);
        var midDistance = reach * Math.Cos(pitch);
        var cameraHeight = reach * Math.Sin(pitch);
        var focal = Focal(area, camera, width, height, fitArea);

        var camX = areaX - fx * midDistance;
        var camY = areaY - fy * midDistance;
        var camZ = baseLevel + cameraHeight;
        var horizon = height * camera.CenterY - focal * Math.Tan(pitch);

        // Höjdöverdriften faller ut ur reliefen: platt mark får mer, dramatisk mindre.
        var vex = Math.Clamp(0.07 * height * midDistance / (focal * Math.Max(relief, 1)), 1.0, vexMax);

        var nearDistance = NearDistance(camera, midDistance, along, focal, height, fitArea);
        var farDistance = midDistance + along * camera.Back;

        // Djupstegen tätnar mot kameran och går fjärran -> nära, så suffixminimeringen nedan
        // alltid ser "allt som ligger närmare".
        var distances = new double[Steps];
        for (var i = 0; i < Steps; i++)
        {
            var t = Math.Pow((Steps - 1.0 - i) / (Steps - 1.0), 1.4);
            distances[i] = nearDistance + t * (farDistance - nearDistance);
        }

        var screenY = new float[Steps * width];
        var colors = new float[Steps * width * 3];
        Parallel.For(0, Steps, i =>
        {
            var distance = distances[i];
            var haze = Math.Pow(Math.Clamp((distance - nearDistance) / (farDistance - nearDistance), 0, 1), 1.7)
                * light.HazeStrength;
            var row = i * width;
            for (var c = 0; c < width; c++)
            {
                var offset = (c - width / 2.0) * distance / focal;
                var east = camX + fx * distance + rx * offset;
                var north = camY + fy * distance + ry * offset;
                var px = (east - bounds.MinX) / bounds.Width * (gridWidth - 1);
                var py = (bounds.MaxY - north) / bounds.Height * (gridHeight - 1);
                var inside = px >= 0 && px <= gridWidth - 1 && py >= 0 && py <= gridHeight - 1;

                Vector3 color;
                if (inside)
                {
                    var (r, g, b) = texture.Sample(px, py);
                    color = new Vector3(r, g, b) * (float)(1 - haze) + light.Haze * (float)haze;
                    var z = elevation.Sample(px, py);
                    screenY[row + c] = (float)(horizon - (baseLevel + (z - baseLevel) * vex - camZ) * focal / distance);
                }
                else
                {
                    color = light.Haze;
                    screenY[row + c] = 1e6f;
                }
                colors[(row + c) * 3] = color.X;
                colors[(row + c) * 3 + 1] = color.Y;
                colors[(row + c) * 3 + 2] = color.Z;
            }
        });

        // Suffixminimum längs djupet: efteråt är varje rad "lägsta skärmrad som någon punkt
        // på det här avståndet eller närmare når", stigande i djupled — och en sökning per
        // bildkolumn ger direkt det närmaste djupsteg som täcker pixeln.
        for (var i = Steps - 2; i >= 0; i--)
        {
            var row = i * width;
            var nearer = row + width;
            for (var c = 0; c < width; c++)
                screenY[row + c] = Math.Min(screenY[row + c], screenY[nearer + c]);
        }

        var image = new ColorGrid(width, height);
        var depth = new ScalarGrid(width, height);
        var top = light.Haze * (light.Night ? 0.45f : 0.86f);
        var bottom = light.Haze * (light.Night ? 1.05f : 1.12f);
        Parallel.For(0, width, c =>
        {
            var step = 0;
            for (var y = 0; y < height; y++)
            {
                while (step < Steps && screenY[step * width + c] <= y)
                    step++;
                var hit = step - 1;
                var i = (y * width + c) * 3;
                if (hit >= 0)
                {
                    image.Values[i] = Math.Clamp(colors[(hit * width + c) * 3], 0f, 1f);
                    image.Values[i + 1] = Math.Clamp(colors[(hit * width + c) * 3 + 1], 0f, 1f);
                    image.Values[i + 2] = Math.Clamp(colors[(hit * width + c) * 3 + 2], 0f, 1f);
                    depth[c, y] = (float)distances[hit];
                }
                else
                {
                    var ramp = (float)y / (height - 1);
                    var sky = Vector3.Clamp(top + (bottom - top) * ramp, Vector3.Zero, Vector3.One);
                    image.Values[i] = sky.X;
                    image.Values[i + 1] = sky.Y;
                    image.Values[i + 2] = sky.Z;
                    depth[c, y] = float.PositiveInfinity;
                }
            }
        });

        return new RenderResult
        {
            Image = image,
            Depth = depth,
            Vex = vex,
            Relief = relief,
            CameraHeight = cameraHeight,
            MidDistance = midDistance,
            Project = (east, north, groundZ) =>
            {
                var vx = east - camX;
                var vy = north - camY;
                var distance = vx * fx + vy * fy;
                if (distance <= 1.0)
                    return null;
                var offset = vx * rx + vy * ry;
                return (width / 2.0 + offset * focal / distance,
                        horizon - (baseLevel + (groundZ - baseLevel) * vex - camZ) * focal / distance,
                        distance);
            },
        };
    }
}
