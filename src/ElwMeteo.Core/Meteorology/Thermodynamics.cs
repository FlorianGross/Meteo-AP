namespace ElwMeteo.Core.Meteorology;

/// <summary>
/// Derived thermodynamic quantities. All temperatures are in °C, wind speeds in
/// m/s, humidity in percent and pressures in hPa unless a name says otherwise.
/// </summary>
public static class Thermodynamics
{
    /// <summary>Saturation vapour pressure over water (Magnus formula), hPa.</summary>
    public static double SaturationVapourPressureHpa(double temperatureC) =>
        6.112 * Math.Exp(17.62 * temperatureC / (243.12 + temperatureC));

    /// <summary>Actual vapour pressure, hPa.</summary>
    public static double VapourPressureHpa(double temperatureC, double relativeHumidityPercent) =>
        SaturationVapourPressureHpa(temperatureC) * Math.Clamp(relativeHumidityPercent, 0.0, 100.0) / 100.0;

    /// <summary>Dew point, °C.</summary>
    public static double DewPointC(double temperatureC, double relativeHumidityPercent)
    {
        double e = VapourPressureHpa(temperatureC, relativeHumidityPercent);
        if (e <= 0)
        {
            return double.NaN;
        }

        double ln = Math.Log(e / 6.112);
        return 243.12 * ln / (17.62 - ln);
    }

    /// <summary>Absolute humidity, g/m³ — how much water a room actually holds.</summary>
    public static double AbsoluteHumidityGm3(double temperatureC, double relativeHumidityPercent)
    {
        double e = VapourPressureHpa(temperatureC, relativeHumidityPercent);
        return 216.7 * e / (273.15 + temperatureC);
    }

    /// <summary>
    /// Wet-bulb temperature after Stull (2011). Valid for roughly 5 % ≤ RH ≤ 99 %
    /// and −20 °C ≤ T ≤ 50 °C at sea level.
    /// </summary>
    public static double WetBulbC(double temperatureC, double relativeHumidityPercent)
    {
        double rh = Math.Clamp(relativeHumidityPercent, 1.0, 100.0);
        return temperatureC * Math.Atan(0.151977 * Math.Sqrt(rh + 8.313659))
               + Math.Atan(temperatureC + rh)
               - Math.Atan(rh - 1.676331)
               + 0.00391838 * Math.Pow(rh, 1.5) * Math.Atan(0.023101 * rh)
               - 4.686035;
    }

    /// <summary>
    /// Shade wet-bulb globe temperature approximation (Australian BoM form).
    /// Used here as a load indicator for crews working under breathing apparatus.
    /// Not valid in direct sun — it deliberately under-reads there.
    /// </summary>
    public static double WbgtShadeC(double temperatureC, double relativeHumidityPercent)
    {
        double e = VapourPressureHpa(temperatureC, relativeHumidityPercent);
        return 0.567 * temperatureC + 0.393 * e + 3.94;
    }

    /// <summary>Canadian wind chill, °C. Only defined for T ≤ 10 °C and wind ≥ 4.8 km/h.</summary>
    public static double? WindChillC(double temperatureC, double windSpeedMs)
    {
        double kmh = WindScale.MsToKmh(windSpeedMs);
        if (temperatureC > 10.0 || kmh < 4.8)
        {
            return null;
        }

        double v = Math.Pow(kmh, 0.16);
        return 13.12 + 0.6215 * temperatureC - 11.37 * v + 0.3965 * temperatureC * v;
    }

    /// <summary>NWS heat index (Rothfusz), °C. Only defined from about 27 °C upwards.</summary>
    public static double? HeatIndexC(double temperatureC, double relativeHumidityPercent)
    {
        if (temperatureC < 26.7)
        {
            return null;
        }

        double t = temperatureC * 9.0 / 5.0 + 32.0; // °F
        double r = Math.Clamp(relativeHumidityPercent, 0.0, 100.0);

        double hi = -42.379
                    + 2.04901523 * t
                    + 10.14333127 * r
                    - 0.22475541 * t * r
                    - 0.00683783 * t * t
                    - 0.05481717 * r * r
                    + 0.00122874 * t * t * r
                    + 0.00085282 * t * r * r
                    - 0.00000199 * t * t * r * r;

        // Rothfusz adjustments at the edges of the fit.
        if (r < 13.0 && t is >= 80.0 and <= 112.0)
        {
            hi -= (13.0 - r) / 4.0 * Math.Sqrt((17.0 - Math.Abs(t - 95.0)) / 17.0);
        }
        else if (r > 85.0 && t is >= 80.0 and <= 87.0)
        {
            hi += (r - 85.0) / 10.0 * ((87.0 - t) / 5.0);
        }

        return (hi - 32.0) * 5.0 / 9.0;
    }

    /// <summary>Canadian humidex, °C.</summary>
    public static double Humidex(double temperatureC, double relativeHumidityPercent)
    {
        double e = VapourPressureHpa(temperatureC, relativeHumidityPercent);
        return temperatureC + 0.5555 * (e - 10.0);
    }

    /// <summary>
    /// Station pressure reduced to mean sea level, hPa — the value that makes
    /// readings from different sites comparable.
    /// </summary>
    public static double ReduceToSeaLevelHpa(double stationPressureHpa, double altitudeM, double temperatureC) =>
        stationPressureHpa * Math.Pow(1.0 - 0.0065 * altitudeM / (temperatureC + 0.0065 * altitudeM + 273.15), -5.257);

    /// <summary>
    /// Height above ground at which rising warm air (and with it smoke) stops
    /// climbing, estimated from the spread between temperature and dew point.
    /// Rule of thumb used in aviation: about 125 m per K of spread.
    /// </summary>
    public static double ConvectiveCloudBaseM(double temperatureC, double dewPointC) =>
        Math.Max(0.0, (temperatureC - dewPointC) * 125.0);
}
