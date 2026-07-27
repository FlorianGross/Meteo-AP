namespace ElwMeteo.Core.Meteorology;

public enum FireRiskLevel
{
    VeryLow,
    Low,
    Moderate,
    High,
    VeryHigh
}

public sealed record FireRiskAssessment(double AngstromIndex, FireRiskLevel Level, string Label, string Note);

/// <summary>
/// Vegetation-fire likelihood from the Ångström index — a two-input screening
/// figure computed from temperature and relative humidity alone.
///
/// This is <em>not</em> the DWD Waldbrandgefahrenindex (WBI), which additionally
/// accounts for soil moisture and precipitation history. Use it as an on-scene
/// plausibility check; the WBI layer on the map tab remains the authority.
/// </summary>
public static class FireRisk
{
    public static FireRiskAssessment Assess(double temperatureC, double relativeHumidityPercent)
    {
        double rh = Math.Clamp(relativeHumidityPercent, 0.0, 100.0);
        double index = rh / 20.0 + (27.0 - temperatureC) / 10.0;

        // Lower index means drier, hotter, more fire-prone.
        (FireRiskLevel level, string label, string note) = index switch
        {
            >= 4.0 => (FireRiskLevel.VeryLow, "sehr gering",
                "Vegetationsbrand unwahrscheinlich."),
            >= 3.0 => (FireRiskLevel.Low, "gering",
                "Entstehung möglich, Ausbreitung träge."),
            >= 2.5 => (FireRiskLevel.Moderate, "mäßig",
                "Bodenfeuer breitet sich stetig aus, Flugfeuer beachten."),
            >= 2.0 => (FireRiskLevel.High, "hoch",
                "Rasche Ausbreitung möglich — Riegelstellung und Wasserversorgung früh planen."),
            _ => (FireRiskLevel.VeryHigh, "sehr hoch",
                "ACHTUNG: extreme Bedingungen. Mit Sprung- und Flugfeuer sowie Eigengefährdung rechnen.")
        };

        return new FireRiskAssessment(index, level, label, note);
    }
}
