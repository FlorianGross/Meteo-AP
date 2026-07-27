using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Time;

namespace ElwMeteo.Core.Assessment;

public enum HintSeverity
{
    Info,
    Caution,
    Warning
}

/// <summary>A single operational note derived from the weather data.</summary>
public sealed record TacticalHint(HintSeverity Severity, string Topic, string Text);

/// <summary>
/// The interpreted view of a <see cref="WeatherSnapshot"/>: derived values plus
/// the operational notes shown on the dashboard.
/// </summary>
public sealed record TacticalAssessment
{
    public required WeatherSnapshot Snapshot { get; init; }

    public required StabilityAssessment Stability { get; init; }

    public required BeaufortStep Beaufort { get; init; }

    public required string WindFromCompass { get; init; }

    public required double DownwindBearingDeg { get; init; }

    public required string DownwindCompass { get; init; }

    public required FireRiskAssessment FireRisk { get; init; }

    public required SolarPosition SolarPosition { get; init; }

    public required SolarDay SolarDay { get; init; }

    public required MoonPhase Moon { get; init; }

    public double? WindChillC { get; init; }

    public double? HeatIndexC { get; init; }

    public double? WbgtShadeC { get; init; }

    public double? WetBulbC { get; init; }

    public double? AbsoluteHumidityGm3 { get; init; }

    public double? ConvectiveCloudBaseM { get; init; }

    /// <summary>Minutes until precipitation starts or stops, from the 15-minute nowcast.</summary>
    public NowcastOutlook Outlook { get; init; } = NowcastOutlook.Unknown;

    /// <summary>Forecast change of wind direction, or null when the wind holds.</summary>
    public WindShift? WindShift { get; init; }

    /// <summary>Strongest gust expected in the look-ahead window.</summary>
    public GustPeak? GustPeak { get; init; }

    public IReadOnlyList<TacticalHint> Hints { get; init; } = [];

    public HintSeverity WorstSeverity =>
        Hints.Count == 0 ? HintSeverity.Info : Hints.Max(h => h.Severity);
}

/// <summary>What the next couple of hours look like precipitation-wise.</summary>
public sealed record NowcastOutlook(
    bool RainingNow,
    TimeSpan? StartsIn,
    TimeSpan? StopsIn,
    double PeakIntensityMmPerHour,
    string Summary)
{
    public static NowcastOutlook Unknown { get; } =
        new(false, null, null, 0.0, "Kein Nowcast verfügbar.");
}

public static class WeatherAssessor
{
    /// <summary>Gust exceedance that makes aerial-ladder and crane work a topic.</summary>
    private const double AerialLadderGustLimitMs = 12.5; // ~45 km/h, typical DIN EN 14043 limit

    public static TacticalAssessment Assess(WeatherSnapshot snapshot, DateTimeOffset now)
    {
        double temperature = snapshot.TemperatureC ?? double.NaN;
        double humidity = snapshot.RelativeHumidityPercent ?? double.NaN;
        double windSpeed = snapshot.WindSpeedMs ?? 0.0;
        double windDirection = snapshot.WindDirectionDeg ?? 0.0;
        double cloudCover = snapshot.CloudCoverPercent ?? 50.0;

        var solarPosition = SolarCalculator.CalculatePosition(
            snapshot.Position.Latitude, snapshot.Position.Longitude, now);
        var solarDay = SolarCalculator.CalculateDay(
            snapshot.Position.Latitude, snapshot.Position.Longitude, now);

        var stability = StabilityClassifier.Classify(windSpeed, cloudCover, solarPosition.ElevationDeg);
        double downwind = WindScale.DownwindDirection(windDirection);

        bool hasTemperature = !double.IsNaN(temperature);
        bool hasHumidity = !double.IsNaN(humidity);
        bool hasBoth = hasTemperature && hasHumidity;

        double? dewPoint = snapshot.DewPointC
                           ?? (hasBoth ? Thermodynamics.DewPointC(temperature, humidity) : null);

        var assessment = new TacticalAssessment
        {
            Snapshot = snapshot,
            Stability = stability,
            Beaufort = WindScale.Beaufort(windSpeed),
            WindFromCompass = WindScale.CompassPoint(windDirection),
            DownwindBearingDeg = downwind,
            DownwindCompass = WindScale.CompassPoint(downwind),
            FireRisk = FireRisk.Assess(
                hasTemperature ? temperature : 15.0,
                hasHumidity ? humidity : 70.0),
            SolarPosition = solarPosition,
            SolarDay = solarDay,
            Moon = MoonCalculator.Calculate(now),
            WindChillC = hasTemperature ? Thermodynamics.WindChillC(temperature, windSpeed) : null,
            HeatIndexC = hasBoth ? Thermodynamics.HeatIndexC(temperature, humidity) : null,
            WbgtShadeC = hasBoth ? Thermodynamics.WbgtShadeC(temperature, humidity) : null,
            WetBulbC = hasBoth ? Thermodynamics.WetBulbC(temperature, humidity) : null,
            AbsoluteHumidityGm3 = hasBoth ? Thermodynamics.AbsoluteHumidityGm3(temperature, humidity) : null,
            ConvectiveCloudBaseM = hasTemperature && dewPoint is not null
                ? Thermodynamics.ConvectiveCloudBaseM(temperature, dewPoint.Value)
                : null,
            Outlook = BuildOutlook(snapshot.Nowcast, now),
            WindShift = WindShiftDetector.Detect(
                windDirection,
                snapshot.Hourly.Select(h => (h.Time, h.WindDirectionDeg, h.WindSpeedMs)),
                now,
                WindLookAhead),
            GustPeak = WindShiftDetector.PeakGust(
                snapshot.Hourly.Select(h => (h.Time, h.WindGustMs)),
                now,
                WindLookAhead)
        };

        return assessment with { Hints = BuildHints(assessment, now) };
    }

    /// <summary>How far ahead wind shifts and gust peaks are looked for.</summary>
    private static readonly TimeSpan WindLookAhead = TimeSpan.FromHours(6);

    /// <summary>
    /// Reads the 15-minute nowcast and reduces it to the two questions actually
    /// asked on scene: is it raining, and when does that change?
    /// </summary>
    public static NowcastOutlook BuildOutlook(IReadOnlyList<NowcastStep> nowcast, DateTimeOffset now)
    {
        // Only steps from the current quarter-hour onwards are of interest.
        var upcoming = nowcast
            .Where(step => step.Time >= now - TimeSpan.FromMinutes(15))
            .OrderBy(step => step.Time)
            .Take(12) // three hours
            .ToList();

        if (upcoming.Count == 0)
        {
            return NowcastOutlook.Unknown;
        }

        bool rainingNow = upcoming[0].HasPrecipitation;

        // A 15-minute total of x mm corresponds to 4x mm/h.
        double peak = upcoming.Max(step => step.PrecipitationMm ?? 0.0) * 4.0;

        TimeSpan? startsIn = null;
        TimeSpan? stopsIn = null;

        if (rainingNow)
        {
            var firstDry = upcoming.FirstOrDefault(step => !step.HasPrecipitation);
            if (firstDry is not null)
            {
                stopsIn = Floor(firstDry.Time - now);
            }
        }
        else
        {
            var firstWet = upcoming.FirstOrDefault(step => step.HasPrecipitation);
            if (firstWet is not null)
            {
                startsIn = Floor(firstWet.Time - now);
            }
        }

        string summary = (rainingNow, startsIn, stopsIn) switch
        {
            (true, _, { } stops) => $"Niederschlag lässt in ca. {Minutes(stops)} min nach (Spitze {peak:F1} mm/h).",
            (true, _, null) => $"Anhaltender Niederschlag über den gesamten Nowcast (Spitze {peak:F1} mm/h).",
            (false, { } starts, _) => $"Niederschlag beginnt in ca. {Minutes(starts)} min (Spitze {peak:F1} mm/h).",
            _ => "In den nächsten Stunden kein Niederschlag erwartet."
        };

        return new NowcastOutlook(rainingNow, startsIn, stopsIn, peak, summary);

        static TimeSpan Floor(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

        static int Minutes(TimeSpan value) => (int)Math.Round(value.TotalMinutes);
    }

    private static List<TacticalHint> BuildHints(TacticalAssessment a, DateTimeOffset now)
    {
        var hints = new List<TacticalHint>();
        WeatherSnapshot s = a.Snapshot;

        // --- Wind ---------------------------------------------------------
        double gust = s.WindGustMs ?? s.WindSpeedMs ?? 0.0;
        if (gust >= AerialLadderGustLimitMs)
        {
            hints.Add(new TacticalHint(
                gust >= 17.2 ? HintSeverity.Warning : HintSeverity.Caution,
                "Drehleiter",
                $"Böen {WindScale.MsToKmh(gust):F0} km/h — Einsatzgrenze vieler Hubrettungsfahrzeuge (ca. {WindScale.MsToKmh(AerialLadderGustLimitMs):F0} km/h) erreicht. Herstellerangaben prüfen."));
        }

        if (a.Beaufort.Force >= 8)
        {
            hints.Add(new TacticalHint(HintSeverity.Warning, "Sturm",
                $"{a.Beaufort.Description} ({a.Beaufort.Force} Bft). Mit Astbruch, gelösten Bauteilen und Folgeeinsätzen rechnen."));
        }

        if ((s.WindSpeedMs ?? 0.0) <= 0.5)
        {
            hints.Add(new TacticalHint(HintSeverity.Caution, "Windstille",
                "Nahezu windstill — keine verlässliche Ausbreitungsrichtung. Gefahrenbereich rundum absperren."));
        }

        // A turning wind moves the plume onto different streets — the single most
        // consequential forecast change for an ongoing hazardous-material incident.
        if (a.WindShift is { } shift)
        {
            hints.Add(new TacticalHint(
                shift.AbsoluteDeltaDeg >= 90 ? HintSeverity.Warning : HintSeverity.Caution,
                "Winddreher",
                $"{shift.Describe(now)} Absperrgrenzen und Evakuierungsbereich rechtzeitig nachführen."));
        }

        // Worth stating separately when the peak is clearly above what blows now.
        if (a.GustPeak is { } peak && peak.GustMs >= 15.0 && peak.GustMs > (s.WindGustMs ?? 0) + 2.0)
        {
            hints.Add(new TacticalHint(HintSeverity.Caution, "Böenentwicklung",
                $"{peak.Describe(now)} Aufbau von Lichtmasten, Zelten und Sprungpolstern entsprechend planen."));
        }

        // --- Stability / hazardous materials ------------------------------
        if (a.Stability.Pasquill is PasquillClass.E or PasquillClass.F)
        {
            hints.Add(new TacticalHint(HintSeverity.Warning, "Gefahrstoff",
                $"Ausbreitungsklasse {a.Stability.KlugManier} ({a.Stability.Label}). {a.Stability.TacticalNote}"));
        }

        // --- Thunderstorm potential ---------------------------------------
        double cape = s.CapeJkg ?? 0.0;
        if (cape >= 1500)
        {
            hints.Add(new TacticalHint(HintSeverity.Warning, "Gewitter",
                $"CAPE {cape:F0} J/kg — hohes Potenzial für kräftige Gewitter mit Starkregen und Sturmböen."));
        }
        else if (cape >= 800)
        {
            hints.Add(new TacticalHint(HintSeverity.Caution, "Gewitter",
                $"CAPE {cape:F0} J/kg — Gewitterentwicklung möglich. Höhenrettung und Drehleitereinsatz vorplanen."));
        }

        if (a.Snapshot.Nowcast.Any(step => step.LightningPotential is >= 1.0))
        {
            hints.Add(new TacticalHint(HintSeverity.Warning, "Blitzschlag",
                "Modell weist Blitzpotenzial im Nowcast aus. Arbeiten in exponierter Lage und an Steckleitern kritisch prüfen."));
        }

        // --- Temperature load ---------------------------------------------
        if (a.WbgtShadeC is { } wbgt)
        {
            if (wbgt >= 30.0)
            {
                hints.Add(new TacticalHint(HintSeverity.Warning, "Hitzebelastung",
                    $"WBGT (Schatten) {wbgt:F1} °C. Atemschutzeinsatzzeiten verkürzen, Bereitstellungsraum beschatten, Trinkpausen erzwingen."));
            }
            else if (wbgt >= 26.0)
            {
                hints.Add(new TacticalHint(HintSeverity.Caution, "Hitzebelastung",
                    $"WBGT (Schatten) {wbgt:F1} °C. Erhöhte Wärmebelastung unter PSA — Rückhaltezeiten im Auge behalten."));
            }
        }

        if (s.TemperatureC is { } temp)
        {
            if (temp <= 3.0)
            {
                hints.Add(new TacticalHint(
                    temp <= 0.0 ? HintSeverity.Warning : HintSeverity.Caution,
                    "Glättegefahr",
                    $"{temp:F1} °C — Löschwasser gefriert auf Verkehrsflächen. Streumittel und Absicherung einplanen."));
            }

            if (temp <= -5.0)
            {
                hints.Add(new TacticalHint(HintSeverity.Warning, "Frost",
                    "Strenger Frost: Pumpen und Schlauchleitungen gegen Einfrieren sichern, Wasserförderung nicht stagnieren lassen."));
            }
        }

        if (a.WindChillC is { } chill && chill <= -10.0)
        {
            hints.Add(new TacticalHint(HintSeverity.Caution, "Auskühlung",
                $"Windchill {chill:F0} °C — Ablösung der Einsatzkräfte engmaschiger planen."));
        }

        // --- Visibility ---------------------------------------------------
        if (s.VisibilityM is { } visibility && visibility < 1000)
        {
            hints.Add(new TacticalHint(
                visibility < 200 ? HintSeverity.Warning : HintSeverity.Caution,
                "Sichtweite",
                $"Sichtweite {visibility:F0} m. Anfahrt und Absicherung der Einsatzstelle anpassen, Warnkleidung und Blaulichtkette beachten."));
        }

        // --- Vegetation fire ----------------------------------------------
        if (a.FireRisk.Level >= FireRiskLevel.High)
        {
            hints.Add(new TacticalHint(
                a.FireRisk.Level == FireRiskLevel.VeryHigh ? HintSeverity.Warning : HintSeverity.Caution,
                "Vegetationsbrand",
                $"Ångström-Index {a.FireRisk.AngstromIndex:F1} ({a.FireRisk.Label}). {a.FireRisk.Note}"));
        }

        // --- Darkness -----------------------------------------------------
        if (a.SolarDay.Sunset is { } sunset && a.SolarPosition.IsDaylight)
        {
            TimeSpan untilDark = sunset - now;
            if (untilDark > TimeSpan.Zero && untilDark <= TimeSpan.FromHours(1))
            {
                hints.Add(new TacticalHint(HintSeverity.Info, "Dunkelheit",
                    $"Sonnenuntergang in {(int)untilDark.TotalMinutes} min — Ausleuchtung und Lichtmast frühzeitig aufbauen."));
            }
        }

        // --- Precipitation ------------------------------------------------
        if (a.Outlook.PeakIntensityMmPerHour >= 15.0)
        {
            hints.Add(new TacticalHint(HintSeverity.Warning, "Starkregen",
                $"Nowcast-Spitze {a.Outlook.PeakIntensityMmPerHour:F0} mm/h. Mit Überflutung von Unterführungen und Kellern rechnen."));
        }

        if (WeatherCodes.IsSignificant(s.WeatherCode))
        {
            hints.Add(new TacticalHint(HintSeverity.Caution, "Wetterlage",
                $"Aktuell {WeatherCodes.Describe(s.WeatherCode)}."));
        }

        return hints
            .OrderByDescending(h => h.Severity)
            .ToList();
    }
}
