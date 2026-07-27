namespace ElwMeteo.Core.Meteorology;

/// <summary>A latitude/longitude pair in degrees (WGS 84).</summary>
public readonly record struct LatLon(double Latitude, double Longitude)
{
    public override string ToString() => $"{Latitude:F5}, {Longitude:F5}";
}

/// <summary>Great-circle helpers on a spherical earth — accurate to ~0.3 % over tactical distances.</summary>
public static class Geodesy
{
    public const double EarthRadiusMetres = 6_371_008.8;

    private static double Rad(double deg) => deg * Math.PI / 180.0;

    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Point reached by travelling <paramref name="distanceM"/> along <paramref name="bearingDeg"/>.</summary>
    public static LatLon Destination(LatLon origin, double bearingDeg, double distanceM)
    {
        double angular = distanceM / EarthRadiusMetres;
        double bearing = Rad(bearingDeg);
        double lat1 = Rad(origin.Latitude);
        double lon1 = Rad(origin.Longitude);

        double sinLat2 = Math.Sin(lat1) * Math.Cos(angular)
                         + Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing);
        double lat2 = Math.Asin(Math.Clamp(sinLat2, -1.0, 1.0));

        double y = Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1);
        double x = Math.Cos(angular) - Math.Sin(lat1) * sinLat2;
        double lon2 = lon1 + Math.Atan2(y, x);

        // Keep longitude in [-180, 180).
        double lonDeg = (Deg(lon2) + 540.0) % 360.0 - 180.0;
        return new LatLon(Deg(lat2), lonDeg);
    }

    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceMetres(LatLon from, LatLon to)
    {
        double lat1 = Rad(from.Latitude);
        double lat2 = Rad(to.Latitude);
        double dLat = lat2 - lat1;
        double dLon = Rad(to.Longitude - from.Longitude);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2.0 * EarthRadiusMetres * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Initial bearing in degrees from north.</summary>
    public static double BearingDeg(LatLon from, LatLon to)
    {
        double lat1 = Rad(from.Latitude);
        double lat2 = Rad(to.Latitude);
        double dLon = Rad(to.Longitude - from.Longitude);

        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        return WindScale.Normalize(Deg(Math.Atan2(y, x)));
    }

    /// <summary>
    /// Formats a position in the degrees/decimal-minutes notation used on
    /// nautical and aeronautical charts and by most handheld GPS units.
    /// </summary>
    public static string FormatDegreesDecimalMinutes(LatLon position)
    {
        return $"{Component(position.Latitude, 'N', 'S', 2)} {Component(position.Longitude, 'E', 'W', 3)}";

        static string Component(double value, char positive, char negative, int degreeDigits)
        {
            char hemisphere = value >= 0 ? positive : negative;
            double absolute = Math.Abs(value);
            int degrees = (int)absolute;
            double minutes = (absolute - degrees) * 60.0;

            // Carry a rounding overflow (59.999' -> 60.000') into the degrees.
            if (Math.Round(minutes, 3) >= 60.0)
            {
                degrees += 1;
                minutes = 0.0;
            }

            return $"{hemisphere} {degrees.ToString(new string('0', degreeDigits))}° {minutes:00.000}'";
        }
    }
}
