namespace ElwMeteo.Core.Time;

public sealed record MoonPhase(double AgeDays, double IlluminatedFraction, string Name)
{
    /// <summary>Illumination as a percentage, rounded for display.</summary>
    public int IlluminatedPercent => (int)Math.Round(IlluminatedFraction * 100.0);
}

/// <summary>
/// Moon age and illumination from the mean synodic cycle. This is an
/// approximation (±~0.5 days) — plenty for judging natural illumination during
/// a night-time search, not suitable for astronomy.
/// </summary>
public static class MoonCalculator
{
    private const double SynodicMonthDays = 29.530588853;

    /// <summary>A reference new moon: 6 January 2000, 18:14 UTC.</summary>
    private static readonly DateTime ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    public static MoonPhase Calculate(DateTimeOffset instant)
    {
        double daysSinceReference = (instant.UtcDateTime - ReferenceNewMoonUtc).TotalDays;
        double age = daysSinceReference % SynodicMonthDays;
        if (age < 0)
        {
            age += SynodicMonthDays;
        }

        // Illuminated fraction of the disc for a circular orbit approximation.
        double illuminated = (1.0 - Math.Cos(2.0 * Math.PI * age / SynodicMonthDays)) / 2.0;

        return new MoonPhase(age, illuminated, NameForAge(age));
    }

    private static string NameForAge(double ageDays)
    {
        // Eight equal segments of the synodic month, centred on the named phases.
        double segment = SynodicMonthDays / 8.0;
        int index = (int)Math.Floor((ageDays + segment / 2.0) / segment) % 8;

        return index switch
        {
            0 => "Neumond",
            1 => "zunehmende Sichel",
            2 => "erstes Viertel",
            3 => "zunehmender Mond",
            4 => "Vollmond",
            5 => "abnehmender Mond",
            6 => "letztes Viertel",
            _ => "abnehmende Sichel"
        };
    }
}
