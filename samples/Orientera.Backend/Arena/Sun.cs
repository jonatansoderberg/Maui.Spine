namespace Orientera.Backend.Arena;

/// <summary>
/// Solens läge ur tävlingens datum och plats. Tävlingsbilden ska visa det ljus löparna
/// faktiskt får — en kvällstävling i slutet av augusti på 60° nord har solen några grader
/// över horisonten, och det är gyllene timme på riktigt, inte som stilval.
/// </summary>
/// <remarks>
/// NOAA:s algoritm, förenklad men trogen på bågminuten — mer än nog för att sätta ljuset i
/// en bild. Tiderna är svensk väggklocka, som Eventor anger dem; sommartidsregeln är
/// portad rakt av från prototypen i stället för att gå via <see cref="TimeZoneInfo"/>,
/// så facit och port räknar på exakt samma UTC.
/// </remarks>
public static class Sun
{
    /// <summary>Solhöjd och azimut i grader vid en svensk väggklockstid.</summary>
    public static (double Altitude, double Azimuth) PositionOf(
        double latitude, double longitude, DateTime when)
    {
        var utc = when - TimeSpan.FromHours(SwedishUtcOffset(when));
        var jd = (utc - new DateTime(2000, 1, 1, 12, 0, 0)).TotalSeconds / 86400.0;
        var t = jd / 36525.0;

        var meanLongitude = Radians((280.46646 + t * (36000.76983 + t * 0.0003032)) % 360);
        var meanAnomaly = Radians((357.52911 + t * (35999.05029 - 0.0001537 * t)) % 360);
        var center = Radians(
            Math.Sin(meanAnomaly) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + Math.Sin(2 * meanAnomaly) * (0.019993 - 0.000101 * t)
            + Math.Sin(3 * meanAnomaly) * 0.000289);
        var trueLongitude = meanLongitude + center;
        var omega = Radians(125.04 - 1934.136 * t);
        var apparentLongitude = trueLongitude - Radians(0.00569 + 0.00478 * Math.Sin(omega));
        var obliquity = Radians(23.0 + (26.0 + (21.448 - t * 46.815) / 60.0) / 60.0
                                + 0.00256 * Math.Cos(omega));
        var declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(apparentLongitude));

        const double eccentricity = 0.016708634;
        var y = Math.Tan(obliquity / 2) * Math.Tan(obliquity / 2);
        var equationOfTime = 4 * Degrees(
            y * Math.Sin(2 * meanLongitude) - 2 * eccentricity * Math.Sin(meanAnomaly)
            + 4 * eccentricity * y * Math.Sin(meanAnomaly) * Math.Cos(2 * meanLongitude)
            - 0.5 * y * y * Math.Sin(4 * meanLongitude)
            - 1.25 * eccentricity * eccentricity * Math.Sin(2 * meanAnomaly));

        var minutes = utc.Hour * 60 + utc.Minute + utc.Second / 60.0;
        var hourAngle = Radians((minutes + equationOfTime + 4 * longitude) / 4.0 - 180.0);

        var lat = Radians(latitude);
        var altitude = Math.Asin(Math.Sin(lat) * Math.Sin(declination)
                                 + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle));
        var azimuth = Math.Atan2(Math.Sin(hourAngle),
            Math.Cos(hourAngle) * Math.Sin(lat) - Math.Tan(declination) * Math.Cos(lat));
        return (Degrees(altitude), (Degrees(azimuth) + 180.0) % 360.0);
    }

    /// <summary>
    /// Svensk normaltid eller sommartid. Sommartid gäller från sista söndagen i mars till
    /// sista söndagen i oktober.
    /// </summary>
    public static int SwedishUtcOffset(DateTime when)
    {
        var start = LastSunday(when.Year, 3);
        var end = LastSunday(when.Year, 10);
        return start <= when.Date && when.Date < end ? 2 : 1;
    }

    private static DateTime LastSunday(int year, int month)
    {
        var day = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return day.AddDays(-(int)day.DayOfWeek);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;
    private static double Degrees(double radians) => radians * 180.0 / Math.PI;
}
