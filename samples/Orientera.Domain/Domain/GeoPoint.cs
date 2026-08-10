namespace Orientera.Domain;

/// <summary>A WGS84 position. Deliberately independent of any platform location type.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Great-circle distance in kilometres.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        double dLat = ToRadians(other.Latitude - Latitude);
        double dLon = ToRadians(other.Longitude - Longitude);
        double lat1 = ToRadians(Latitude);
        double lat2 = ToRadians(other.Latitude);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);

        return EarthRadiusKm * 2 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
