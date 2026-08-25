namespace Orientera.Domain;

/// <summary>A WGS84 position. Deliberately independent of any platform location type.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Whether this is a real place at all.
    /// </summary>
    /// <remarks>
    /// A source that has no coordinate leaves the pair at zero, and (0,0) is a point in the Gulf
    /// of Guinea — no arena, no home, and 6905 km from Gävle, which is the distance a competition
    /// without a published arena was claiming in the calendar. Reading zero as "unset" is reading
    /// the encoding the sources already use, the same way <c>HasFirstStart</c> reads midnight.
    /// </remarks>
    public bool IsKnown => Latitude != 0 || Longitude != 0;

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
