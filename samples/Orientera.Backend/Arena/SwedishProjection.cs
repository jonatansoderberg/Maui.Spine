using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace Orientera.Backend.Arena;

/// <summary>
/// WGS84 till SWEREF99 TM och tillbaka. Allt markdatat — höjdmodell, ortofoto, kamerageometri —
/// lever i SWEREF99 TM (EPSG:3006); Eventor talar WGS84.
/// </summary>
/// <remarks>
/// Systemet byggs programmatiskt i stället för att tolkas ur WKT: då finns det ingen talparsning
/// som kan snubbla på svenskt locale. SWEREF99 behandlas som identiskt med WGS84 i datum —
/// samma antagande pyproj gör för EPSG:4326 till 3006, och skillnaden är under en meter.
/// </remarks>
public static class SwedishProjection
{
    private static readonly MathTransform Forward;
    private static readonly MathTransform Inverse;

    static SwedishProjection()
    {
        var factory = new CoordinateSystemFactory();

        var grs80 = factory.CreateFlattenedSphere("GRS 1980", 6378137, 298.257222101, LinearUnit.Metre);
        var datum = factory.CreateHorizontalDatum("SWEREF99", DatumType.HD_Geocentric, grs80, null);
        var geographic = factory.CreateGeographicCoordinateSystem(
            "SWEREF99", AngularUnit.Degrees, datum, PrimeMeridian.Greenwich,
            new AxisInfo("Lon", AxisOrientationEnum.East),
            new AxisInfo("Lat", AxisOrientationEnum.North));

        var projection = factory.CreateProjection("Transverse_Mercator", "Transverse_Mercator",
        [
            new ProjectionParameter("latitude_of_origin", 0),
            new ProjectionParameter("central_meridian", 15),
            new ProjectionParameter("scale_factor", 0.9996),
            new ProjectionParameter("false_easting", 500_000),
            new ProjectionParameter("false_northing", 0),
        ]);

        var sweref = factory.CreateProjectedCoordinateSystem(
            "SWEREF99 TM", geographic, projection, LinearUnit.Metre,
            new AxisInfo("East", AxisOrientationEnum.East),
            new AxisInfo("North", AxisOrientationEnum.North));

        Forward = new CoordinateTransformationFactory()
            .CreateFromCoordinateSystems(GeographicCoordinateSystem.WGS84, sweref)
            .MathTransform;
        Inverse = Forward.Inverse();
    }

    public static (double East, double North) ToSweref(double latitude, double longitude)
    {
        var p = Forward.Transform(new[] { longitude, latitude });
        return (p[0], p[1]);
    }

    public static (double Latitude, double Longitude) ToWgs84(double east, double north)
    {
        var p = Inverse.Transform(new[] { east, north });
        return (p[1], p[0]);
    }
}
