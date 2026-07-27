namespace ElwMeteo.Core.Time;

/// <summary>Sun position and the day's light phases for a geographic position.</summary>
public sealed record SolarDay(
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    DateTimeOffset SolarNoon,
    DateTimeOffset? CivilDawn,
    DateTimeOffset? CivilDusk,
    DateTimeOffset? NauticalDawn,
    DateTimeOffset? NauticalDusk,
    DateTimeOffset? AstronomicalDawn,
    DateTimeOffset? AstronomicalDusk)
{
    /// <summary>Length of the bright day, or null when the sun never rises/sets.</summary>
    public TimeSpan? DayLength => Sunrise is not null && Sunset is not null ? Sunset - Sunrise : null;

    /// <summary>True when the sun stays below the horizon for the whole day.</summary>
    public bool PolarNight => Sunrise is null && Sunset is null && SunNeverRises;

    public bool SunNeverRises { get; init; }

    public bool MidnightSun { get; init; }
}

/// <summary>Where the sun is right now, as seen from a position.</summary>
public sealed record SolarPosition(double ElevationDeg, double AzimuthDeg)
{
    public bool IsDaylight => ElevationDeg > -0.833;
}

/// <summary>
/// NOAA solar-position algorithm. Accurate to well under a minute for the
/// latitudes and time span this application cares about, and — unlike calling a
/// web service — it keeps working when the vehicle has no connectivity.
/// </summary>
public static class SolarCalculator
{
    // Zenith angles that delimit the light phases.
    private const double ZenithSunriseSunset = 90.833; // includes refraction + solar radius
    private const double ZenithCivil = 96.0;
    private const double ZenithNautical = 102.0;
    private const double ZenithAstronomical = 108.0;

    private static double Rad(double deg) => deg * Math.PI / 180.0;

    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Julian day for an instant in UTC.</summary>
    internal static double JulianDay(DateTime utc)
    {
        // Meeus, chapter 7, valid for the Gregorian calendar.
        int year = utc.Year;
        int month = utc.Month;
        double day = utc.Day
                     + utc.Hour / 24.0
                     + utc.Minute / 1440.0
                     + (utc.Second + utc.Millisecond / 1000.0) / 86400.0;

        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }

        int a = year / 100;
        int b = 2 - a + a / 4;

        return Math.Floor(365.25 * (year + 4716))
               + Math.Floor(30.6001 * (month + 1))
               + day + b - 1524.5;
    }

    private static double JulianCentury(double julianDay) => (julianDay - 2451545.0) / 36525.0;

    private static double GeomMeanLongSunDeg(double t) =>
        Mod360(280.46646 + t * (36000.76983 + t * 0.0003032));

    private static double GeomMeanAnomalySunDeg(double t) =>
        357.52911 + t * (35999.05029 - 0.0001537 * t);

    private static double EccentricityEarthOrbit(double t) =>
        0.016708634 - t * (0.000042037 + 0.0000001267 * t);

    private static double SunEquationOfCentre(double t)
    {
        double m = Rad(GeomMeanAnomalySunDeg(t));
        return Math.Sin(m) * (1.914602 - t * (0.004817 + 0.000014 * t))
               + Math.Sin(2 * m) * (0.019993 - 0.000101 * t)
               + Math.Sin(3 * m) * 0.000289;
    }

    private static double SunApparentLongitudeDeg(double t)
    {
        double trueLong = GeomMeanLongSunDeg(t) + SunEquationOfCentre(t);
        return trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(125.04 - 1934.136 * t));
    }

    private static double ObliquityCorrectionDeg(double t)
    {
        double meanObliquity = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
        return meanObliquity + 0.00256 * Math.Cos(Rad(125.04 - 1934.136 * t));
    }

    /// <summary>Solar declination in degrees.</summary>
    internal static double DeclinationDeg(double t) =>
        Deg(Math.Asin(Math.Sin(Rad(ObliquityCorrectionDeg(t))) * Math.Sin(Rad(SunApparentLongitudeDeg(t)))));

    /// <summary>Equation of time in minutes.</summary>
    internal static double EquationOfTimeMinutes(double t)
    {
        double epsilon = ObliquityCorrectionDeg(t);
        double l0 = GeomMeanLongSunDeg(t);
        double e = EccentricityEarthOrbit(t);
        double m = GeomMeanAnomalySunDeg(t);

        double y = Math.Tan(Rad(epsilon / 2.0));
        y *= y;

        double eqTime = y * Math.Sin(2 * Rad(l0))
                        - 2 * e * Math.Sin(Rad(m))
                        + 4 * e * y * Math.Sin(Rad(m)) * Math.Cos(2 * Rad(l0))
                        - 0.5 * y * y * Math.Sin(4 * Rad(l0))
                        - 1.25 * e * e * Math.Sin(2 * Rad(m));

        return 4.0 * Deg(eqTime);
    }

    /// <summary>Hour angle (degrees) at which the sun reaches the given zenith, or null if never.</summary>
    private static double? HourAngleForZenith(double latitudeDeg, double declinationDeg, double zenithDeg)
    {
        double latRad = Rad(latitudeDeg);
        double declRad = Rad(declinationDeg);

        double cosH = (Math.Cos(Rad(zenithDeg)) - Math.Sin(latRad) * Math.Sin(declRad))
                      / (Math.Cos(latRad) * Math.Cos(declRad));

        // |cosH| > 1 means the sun never crosses that zenith on this day.
        if (cosH is > 1.0 or < -1.0)
        {
            return null;
        }

        return Deg(Math.Acos(cosH));
    }

    /// <summary>
    /// Sunrise, sunset, solar noon and the three twilight pairs for the calendar
    /// day of <paramref name="localDate"/> as observed at the given position.
    /// Results are returned in the offset of <paramref name="localDate"/>.
    /// </summary>
    public static SolarDay CalculateDay(double latitudeDeg, double longitudeDeg, DateTimeOffset localDate)
    {
        TimeSpan offset = localDate.Offset;

        // Anchor on local solar noon of the requested calendar day.
        DateTime localMidnight = localDate.Date;
        DateTime utcNoonGuess = new DateTimeOffset(localMidnight.AddHours(12), offset).UtcDateTime;

        double t = JulianCentury(JulianDay(utcNoonGuess));
        double declination = DeclinationDeg(t);
        double eqTime = EquationOfTimeMinutes(t);

        // Minutes past UTC midnight of the *UTC* day that contains solar noon.
        double solarNoonMinutesUtc = 720.0 - 4.0 * longitudeDeg - eqTime;
        DateTime utcDayStart = utcNoonGuess.Date;
        DateTimeOffset solarNoon = ToOffset(utcDayStart, solarNoonMinutesUtc, offset);

        (DateTimeOffset? rise, DateTimeOffset? set) Pair(double zenith)
        {
            double? hourAngle = HourAngleForZenith(latitudeDeg, declination, zenith);
            if (hourAngle is null)
            {
                return (null, null);
            }

            return (ToOffset(utcDayStart, solarNoonMinutesUtc - hourAngle.Value * 4.0, offset),
                    ToOffset(utcDayStart, solarNoonMinutesUtc + hourAngle.Value * 4.0, offset));
        }

        var (sunrise, sunset) = Pair(ZenithSunriseSunset);
        var (civilDawn, civilDusk) = Pair(ZenithCivil);
        var (nauticalDawn, nauticalDusk) = Pair(ZenithNautical);
        var (astroDawn, astroDusk) = Pair(ZenithAstronomical);

        // Distinguish midnight sun from polar night by the sun's noon elevation.
        double noonElevation = 90.0 - Math.Abs(latitudeDeg - declination);
        bool noRiseSet = sunrise is null;

        return new SolarDay(
            sunrise, sunset, solarNoon,
            civilDawn, civilDusk,
            nauticalDawn, nauticalDusk,
            astroDawn, astroDusk)
        {
            SunNeverRises = noRiseSet && noonElevation <= 0,
            MidnightSun = noRiseSet && noonElevation > 0
        };
    }

    /// <summary>Sun elevation and azimuth (clockwise from north) at an instant.</summary>
    public static SolarPosition CalculatePosition(double latitudeDeg, double longitudeDeg, DateTimeOffset instant)
    {
        DateTime utc = instant.UtcDateTime;
        double t = JulianCentury(JulianDay(utc));
        double declination = DeclinationDeg(t);
        double eqTime = EquationOfTimeMinutes(t);

        double minutesUtc = utc.TimeOfDay.TotalMinutes;
        double trueSolarTime = minutesUtc + eqTime + 4.0 * longitudeDeg;
        trueSolarTime = ((trueSolarTime % 1440.0) + 1440.0) % 1440.0;

        double hourAngle = trueSolarTime / 4.0 - 180.0;

        double latRad = Rad(latitudeDeg);
        double declRad = Rad(declination);
        double haRad = Rad(hourAngle);

        double cosZenith = Math.Sin(latRad) * Math.Sin(declRad)
                           + Math.Cos(latRad) * Math.Cos(declRad) * Math.Cos(haRad);
        cosZenith = Math.Clamp(cosZenith, -1.0, 1.0);

        double zenith = Deg(Math.Acos(cosZenith));
        double elevation = 90.0 - zenith + AtmosphericRefractionDeg(90.0 - zenith);

        // Azimuth measured clockwise from true north.
        double azimuth;
        double denominator = Math.Cos(latRad) * Math.Sin(Rad(zenith));
        if (Math.Abs(denominator) > 1e-9)
        {
            double cosAzimuth = (Math.Sin(latRad) * Math.Cos(Rad(zenith)) - Math.Sin(declRad)) / denominator;
            cosAzimuth = Math.Clamp(cosAzimuth, -1.0, 1.0);
            azimuth = Deg(Math.Acos(cosAzimuth));
            azimuth = hourAngle > 0 ? Mod360(azimuth + 180.0) : Mod360(540.0 - azimuth);
        }
        else
        {
            azimuth = latitudeDeg > 0 ? 180.0 : 0.0;
        }

        return new SolarPosition(elevation, azimuth);
    }

    /// <summary>Approximate refraction correction, in degrees, for an uncorrected elevation.</summary>
    private static double AtmosphericRefractionDeg(double elevationDeg)
    {
        if (elevationDeg > 85.0)
        {
            return 0.0;
        }

        double tanElev = Math.Tan(Rad(elevationDeg));

        double correctionArcSeconds = elevationDeg switch
        {
            > 5.0 => 58.1 / tanElev - 0.07 / Math.Pow(tanElev, 3) + 0.000086 / Math.Pow(tanElev, 5),
            > -0.575 => 1735.0 + elevationDeg * (-518.2 + elevationDeg * (103.4 + elevationDeg * (-12.79 + elevationDeg * 0.711))),
            _ => -20.774 / tanElev
        };

        return correctionArcSeconds / 3600.0;
    }

    private static DateTimeOffset ToOffset(DateTime utcDayStart, double minutesPastUtcMidnight, TimeSpan offset)
    {
        DateTime utc = utcDayStart.AddMinutes(minutesPastUtcMidnight);
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(offset);
    }

    private static double Mod360(double value) => ((value % 360.0) + 360.0) % 360.0;
}
