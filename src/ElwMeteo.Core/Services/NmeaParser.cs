using System.Globalization;

namespace ElwMeteo.Core.Services;

/// <summary>A position fix decoded from an NMEA 0183 sentence.</summary>
public sealed record NmeaFix(
    double Latitude,
    double Longitude,
    double? AltitudeM,
    double? HorizontalDilution,
    int? SatelliteCount,
    TimeOnly? UtcTime,
    double? SpeedOverGroundMs,
    double? CourseOverGroundDeg)
{
    /// <summary>
    /// Rough horizontal accuracy in metres from HDOP, using the usual 5 m
    /// user-equivalent range error assumption for consumer receivers.
    /// </summary>
    public double? EstimatedAccuracyM => HorizontalDilution is > 0 ? HorizontalDilution * 5.0 : null;
}

/// <summary>
/// Decoder for the handful of NMEA 0183 sentences a GPS mouse or a vehicle
/// navigation system emits on a serial port. Many command vehicles already have
/// such a receiver, and it beats IP geolocation by three orders of magnitude.
///
/// Supported: GGA (fix with altitude), RMC (fix with speed/course), GLL (fix only).
/// Talker IDs are accepted generically, so GP/GN/GL/GA all work.
/// </summary>
public static class NmeaParser
{
    /// <summary>
    /// Parses a single sentence. Returns null for sentences that are malformed,
    /// unsupported, or report no valid fix — a GPS stream contains plenty of all three.
    /// </summary>
    public static NmeaFix? ParseSentence(string? sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return null;
        }

        string line = sentence.Trim();
        if (!line.StartsWith('$'))
        {
            return null;
        }

        // Strip and verify the "*hh" checksum when present.
        int asterisk = line.IndexOf('*');
        if (asterisk >= 0)
        {
            string payload = line[1..asterisk];
            string checksumText = line[(asterisk + 1)..];
            if (checksumText.Length >= 2 && !ChecksumMatches(payload, checksumText[..2]))
            {
                return null;
            }

            line = line[..asterisk];
        }

        string[] fields = line[1..].Split(',');
        if (fields.Length < 2)
        {
            return null;
        }

        // Field 0 is the five-character address: two-letter talker + three-letter type.
        string type = fields[0].Length >= 5 ? fields[0][2..5].ToUpperInvariant() : fields[0].ToUpperInvariant();

        return type switch
        {
            "GGA" => ParseGga(fields),
            "RMC" => ParseRmc(fields),
            "GLL" => ParseGll(fields),
            _ => null
        };
    }

    // $GPGGA,hhmmss.ss,llll.ll,a,yyyyy.yy,a,q,nn,d.d,h.h,M,...
    private static NmeaFix? ParseGga(string[] f)
    {
        if (f.Length < 10)
        {
            return null;
        }

        // Quality 0 means "no fix".
        if (!int.TryParse(f[6], CultureInfo.InvariantCulture, out int quality) || quality == 0)
        {
            return null;
        }

        double? latitude = ParseCoordinate(f[2], f[3]);
        double? longitude = ParseCoordinate(f[4], f[5]);
        if (latitude is null || longitude is null)
        {
            return null;
        }

        return new NmeaFix(
            latitude.Value,
            longitude.Value,
            ParseDouble(f[9]),
            ParseDouble(f[8]),
            int.TryParse(f[7], CultureInfo.InvariantCulture, out int satellites) ? satellites : null,
            ParseTime(f[1]),
            null,
            null);
    }

    // $GPRMC,hhmmss.ss,A,llll.ll,a,yyyyy.yy,a,speedKnots,courseDeg,ddmmyy,...
    private static NmeaFix? ParseRmc(string[] f)
    {
        if (f.Length < 9)
        {
            return null;
        }

        // Status must be 'A' (active); 'V' means the fix is not valid.
        if (!string.Equals(f[2], "A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        double? latitude = ParseCoordinate(f[3], f[4]);
        double? longitude = ParseCoordinate(f[5], f[6]);
        if (latitude is null || longitude is null)
        {
            return null;
        }

        double? knots = ParseDouble(f[7]);

        return new NmeaFix(
            latitude.Value,
            longitude.Value,
            null,
            null,
            null,
            ParseTime(f[1]),
            knots is null ? null : knots.Value * 0.514444,
            ParseDouble(f[8]));
    }

    // $GPGLL,llll.ll,a,yyyyy.yy,a,hhmmss.ss,A
    private static NmeaFix? ParseGll(string[] f)
    {
        if (f.Length < 7)
        {
            return null;
        }

        // Field 6 is the data-valid flag.
        if (!string.Equals(f[6], "A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        double? latitude = ParseCoordinate(f[1], f[2]);
        double? longitude = ParseCoordinate(f[3], f[4]);
        if (latitude is null || longitude is null)
        {
            return null;
        }

        return new NmeaFix(latitude.Value, longitude.Value, null, null, null, ParseTime(f[5]), null, null);
    }

    /// <summary>
    /// Converts the NMEA "ddmm.mmmm" / "dddmm.mmmm" form plus hemisphere letter
    /// into signed decimal degrees.
    /// </summary>
    internal static double? ParseCoordinate(string value, string hemisphere)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(hemisphere))
        {
            return null;
        }

        int dot = value.IndexOf('.');
        // Degrees occupy everything except the last two digits before the decimal point.
        int degreeDigits = (dot < 0 ? value.Length : dot) - 2;
        if (degreeDigits < 1)
        {
            return null;
        }

        if (!double.TryParse(value[..degreeDigits], NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees) ||
            !double.TryParse(value[degreeDigits..], NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
        {
            return null;
        }

        double result = degrees + minutes / 60.0;

        return hemisphere.Trim().ToUpperInvariant() switch
        {
            "N" or "E" => result,
            "S" or "W" => -result,
            _ => null
        };
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;

    private static TimeOnly? ParseTime(string value)
    {
        if (value.Length < 6)
        {
            return null;
        }

        if (!int.TryParse(value[..2], out int hours) ||
            !int.TryParse(value[2..4], out int minutes) ||
            !double.TryParse(value[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return null;
        }

        if (hours > 23 || minutes > 59 || seconds >= 60.0)
        {
            return null;
        }

        return new TimeOnly(hours, minutes, (int)seconds, (int)((seconds % 1) * 1000));
    }

    private static bool ChecksumMatches(string payload, string expectedHex)
    {
        int checksum = 0;
        foreach (char c in payload)
        {
            checksum ^= c;
        }

        return int.TryParse(expectedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int expected)
               && checksum == expected;
    }
}
