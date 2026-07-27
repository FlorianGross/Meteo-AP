namespace ElwMeteo.Core.Meteorology;

public sealed record BeaufortStep(int Force, string Description);

/// <summary>Beaufort classification and compass-point naming (German labels).</summary>
public static class WindScale
{
    /// <summary>Lower bound in m/s of each Beaufort force, index == force.</summary>
    private static readonly double[] LowerBoundsMs =
    [
        0.0, 0.3, 1.6, 3.4, 5.5, 8.0, 10.8, 13.9, 17.2, 20.8, 24.5, 28.5, 32.7
    ];

    private static readonly string[] Descriptions =
    [
        "Windstille",
        "leiser Zug",
        "leichte Brise",
        "schwache Brise",
        "mäßige Brise",
        "frische Brise",
        "starker Wind",
        "steifer Wind",
        "stürmischer Wind",
        "Sturm",
        "schwerer Sturm",
        "orkanartiger Sturm",
        "Orkan"
    ];

    /// <summary>16-point compass abbreviations, German convention (O instead of E).</summary>
    private static readonly string[] CompassPoints =
    [
        "N", "NNO", "NO", "ONO", "O", "OSO", "SO", "SSO",
        "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
    ];

    public static BeaufortStep Beaufort(double windSpeedMs)
    {
        if (double.IsNaN(windSpeedMs) || windSpeedMs < 0)
        {
            windSpeedMs = 0;
        }

        int force = 0;
        for (int i = LowerBoundsMs.Length - 1; i >= 0; i--)
        {
            if (windSpeedMs >= LowerBoundsMs[i])
            {
                force = i;
                break;
            }
        }

        return new BeaufortStep(force, Descriptions[force]);
    }

    /// <summary>Compass abbreviation for a direction the wind blows *from*.</summary>
    public static string CompassPoint(double directionDeg)
    {
        double normalized = Normalize(directionDeg);
        int index = (int)Math.Round(normalized / 22.5) % 16;
        return CompassPoints[index];
    }

    /// <summary>Direction the wind blows *towards* — the direction a plume travels.</summary>
    public static double DownwindDirection(double windFromDeg) => Normalize(windFromDeg + 180.0);

    public static double MsToKmh(double ms) => ms * 3.6;

    public static double KmhToMs(double kmh) => kmh / 3.6;

    public static double MsToKnots(double ms) => ms * 1.943844;

    public static double Normalize(double degrees)
    {
        double value = degrees % 360.0;
        return value < 0 ? value + 360.0 : value;
    }
}
