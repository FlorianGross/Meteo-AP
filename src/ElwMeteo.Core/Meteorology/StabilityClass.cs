namespace ElwMeteo.Core.Meteorology;

/// <summary>Pasquill-Gifford atmospheric stability categories.</summary>
public enum PasquillClass
{
    /// <summary>A — very unstable, strong vertical mixing.</summary>
    A,
    /// <summary>B — unstable.</summary>
    B,
    /// <summary>C — slightly unstable.</summary>
    C,
    /// <summary>D — neutral.</summary>
    D,
    /// <summary>E — slightly stable.</summary>
    E,
    /// <summary>F — stable, hazardous material stays near the ground.</summary>
    F
}

/// <summary>
/// Result of a stability assessment, carrying both the international Pasquill
/// letter and the German dispersion class after Klug/Manier (VDI 3782), which is
/// the notation used in German hazardous-material dispersion calculations.
/// </summary>
public sealed record StabilityAssessment(
    PasquillClass Pasquill,
    string KlugManier,
    string Label,
    string TacticalNote);

/// <summary>
/// Determines how readily the atmosphere mixes vertically. This governs whether a
/// gas or smoke plume lifts and dilutes quickly or creeps along the ground —
/// the single most important input to any hazard-area estimate.
/// </summary>
public static class StabilityClassifier
{
    /// <summary>
    /// Classify from the values the app already has: 10 m wind speed, total cloud
    /// cover and the sun's elevation.
    /// </summary>
    /// <param name="windSpeedMs">Wind speed at 10 m, m/s.</param>
    /// <param name="cloudCoverPercent">Total cloud cover, 0-100 %.</param>
    /// <param name="solarElevationDeg">Sun elevation above the horizon; negative at night.</param>
    public static StabilityAssessment Classify(double windSpeedMs, double cloudCoverPercent, double solarElevationDeg)
    {
        double wind = double.IsNaN(windSpeedMs) ? 0.0 : Math.Max(0.0, windSpeedMs);
        double cloud = double.IsNaN(cloudCoverPercent) ? 50.0 : Math.Clamp(cloudCoverPercent, 0.0, 100.0);

        bool isDay = solarElevationDeg > 0.0;
        PasquillClass pasquill = isDay
            ? DaytimeClass(wind, Insolation(solarElevationDeg, cloud))
            : NighttimeClass(wind, cloud);

        return Describe(pasquill);
    }

    /// <summary>Incoming solar radiation, graded 3 (strong) down to 0 (overcast).</summary>
    private static int Insolation(double solarElevationDeg, double cloudCoverPercent)
    {
        // Overcast suppresses insolation entirely, day or night: always neutral.
        if (cloudCoverPercent >= 87.5)
        {
            return 0;
        }

        int grade = solarElevationDeg switch
        {
            > 60.0 => 3, // strong
            > 35.0 => 2, // moderate
            > 15.0 => 1, // slight
            _ => 1       // sun low above the horizon
        };

        // Broken cloud knocks the insolation down one step.
        if (cloudCoverPercent >= 50.0)
        {
            grade -= 1;
        }

        return Math.Max(0, grade);
    }

    private static PasquillClass DaytimeClass(double windMs, int insolation)
    {
        if (insolation == 0)
        {
            return PasquillClass.D;
        }

        // Pasquill's original table, resolving the A-B / B-C / C-D cells to the
        // more stable of the two so hazard areas are not underestimated.
        return windMs switch
        {
            < 2.0 => insolation switch
            {
                3 => PasquillClass.A,
                2 => PasquillClass.B,
                _ => PasquillClass.B
            },
            < 3.0 => insolation switch
            {
                3 => PasquillClass.B,
                2 => PasquillClass.B,
                _ => PasquillClass.C
            },
            < 5.0 => insolation switch
            {
                3 => PasquillClass.B,
                2 => PasquillClass.C,
                _ => PasquillClass.C
            },
            < 6.0 => insolation switch
            {
                3 => PasquillClass.C,
                2 => PasquillClass.D,
                _ => PasquillClass.D
            },
            _ => insolation switch
            {
                3 => PasquillClass.C,
                _ => PasquillClass.D
            }
        };
    }

    private static PasquillClass NighttimeClass(double windMs, double cloudCoverPercent)
    {
        // Overcast nights are mechanically mixed — neutral.
        if (cloudCoverPercent >= 87.5)
        {
            return PasquillClass.D;
        }

        bool thinlyClouded = cloudCoverPercent >= 50.0;

        return windMs switch
        {
            < 2.0 => PasquillClass.F,
            < 3.0 => thinlyClouded ? PasquillClass.E : PasquillClass.F,
            < 5.0 => thinlyClouded ? PasquillClass.D : PasquillClass.E,
            _ => PasquillClass.D
        };
    }

    private static StabilityAssessment Describe(PasquillClass pasquill) => pasquill switch
    {
        PasquillClass.A => new StabilityAssessment(pasquill, "V", "sehr labil",
            "Starke Vertikalmischung — Wolke steigt und verdünnt schnell, bodennahe Konzentration sinkt zügig."),
        PasquillClass.B => new StabilityAssessment(pasquill, "IV", "labil",
            "Gute Durchmischung, Ausbreitungswolke wird breit und flach im Anstieg."),
        PasquillClass.C => new StabilityAssessment(pasquill, "III/2", "leicht labil",
            "Mäßige Durchmischung, Fahne folgt weitgehend der Windrichtung."),
        PasquillClass.D => new StabilityAssessment(pasquill, "III/1", "neutral",
            "Neutrale Schichtung — Standardfall, Fahne bleibt schlank und richtungstreu."),
        PasquillClass.E => new StabilityAssessment(pasquill, "II", "leicht stabil",
            "Geringe Vertikalmischung, Verdünnung verlangsamt. Absperrbereich großzügig wählen."),
        _ => new StabilityAssessment(pasquill, "I", "stabil",
            "ACHTUNG: kaum Vertikalmischung. Schwere Gase kriechen bodennah weit — Senken, Keller, Unterführungen prüfen.")
    };
}
