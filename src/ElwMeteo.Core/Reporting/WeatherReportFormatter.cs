using System.Globalization;
using System.Text;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Time;

namespace ElwMeteo.Core.Reporting;

/// <summary>
/// Renders an assessment as plain text for the operations log (Einsatztagebuch)
/// or to read out over the radio. Copying a block instead of retyping numbers is
/// what makes the difference between the app being used and being ignored.
/// </summary>
public static class WeatherReportFormatter
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Full block for pasting into the operations log.</summary>
    public static string BuildLogEntry(TacticalAssessment a, DateTimeOffset now, string? addressLine = null)
    {
        WeatherSnapshot s = a.Snapshot;
        var text = new StringBuilder();

        text.AppendLine($"WETTERMELDUNG  {TacticalTime.FormatLocal(now)}  (Ortszeit {now.ToLocalTime():dd.MM.yyyy HH:mm})");
        text.AppendLine(new string('-', 62));

        text.AppendLine($"Position    : {Geodesy.FormatDegreesDecimalMinutes(s.Position.ToLatLon())}");
        text.AppendLine($"              {s.Position.Latitude.ToString("F5", German)} / {s.Position.Longitude.ToString("F5", German)}  ({s.Position.SourceLabel})");

        if (!string.IsNullOrWhiteSpace(addressLine))
        {
            text.AppendLine($"              {addressLine}");
        }

        text.AppendLine();
        text.AppendLine($"Wetter      : {WeatherCodes.Describe(s.WeatherCode)}");
        text.AppendLine($"Temperatur  : {Value(s.TemperatureC, "F1", "°C")}   (gefühlt {Value(s.ApparentTemperatureC, "F1", "°C")})");
        text.AppendLine($"Feuchte     : {Value(s.RelativeHumidityPercent, "F0", "%")}   Taupunkt {Value(s.DewPointC, "F1", "°C")}");
        text.AppendLine($"Wind        : aus {a.WindFromCompass} ({Value(s.WindDirectionDeg, "F0", "°")}) " +
                        $"{Value(s.WindSpeedMs, "F1", "m/s")} = {Value(s.WindSpeedMs is null ? null : WindScale.MsToKmh(s.WindSpeedMs.Value), "F0", "km/h")}, " +
                        $"{a.Beaufort.Force} Bft ({a.Beaufort.Description})");
        text.AppendLine($"Böen        : {Value(s.WindGustMs, "F1", "m/s")} = {Value(s.WindGustMs is null ? null : WindScale.MsToKmh(s.WindGustMs.Value), "F0", "km/h")}");
        text.AppendLine($"Luftdruck   : {Value(s.PressureMslHpa, "F0", "hPa")} (NN)");
        text.AppendLine($"Bewölkung   : {Value(s.CloudCoverPercent, "F0", "%")}   Sicht {Visibility(s.VisibilityM)}");
        text.AppendLine($"Niederschlag: {Value(s.PrecipitationMm, "F1", "mm/h")}");

        text.AppendLine();
        text.AppendLine("AUSBREITUNG");
        text.AppendLine($"  Ausbreitungsrichtung : nach {a.DownwindCompass} ({a.DownwindBearingDeg.ToString("F0", German)}°)");
        text.AppendLine($"  Ausbreitungsklasse   : {a.Stability.KlugManier} / Pasquill {a.Stability.Pasquill} ({a.Stability.Label})");
        text.AppendLine($"  Hinweis              : {a.Stability.TacticalNote}");

        text.AppendLine();
        text.AppendLine("WINDENTWICKLUNG (6 h)");
        text.AppendLine($"  Winddreher           : {(a.WindShift is { } shift ? shift.Describe(now) : "keine relevante Drehung erwartet")}");
        text.AppendLine($"  Böenspitze           : {(a.GustPeak is { } peak ? peak.Describe(now) : "—")}");

        if (a.WbgtShadeC is { } wbgt)
        {
            text.AppendLine();
            text.AppendLine("BELASTUNG EINSATZKRÄFTE");
            text.AppendLine($"  WBGT (Schatten)      : {wbgt.ToString("F1", German)} °C");
            if (a.HeatIndexC is { } heatIndex)
            {
                text.AppendLine($"  Hitzeindex           : {heatIndex.ToString("F1", German)} °C");
            }

            if (a.WindChillC is { } windChill)
            {
                text.AppendLine($"  Windchill            : {windChill.ToString("F1", German)} °C");
            }
        }

        text.AppendLine();
        text.AppendLine("TAGESLICHT");
        text.AppendLine($"  Sonnenaufgang        : {Clock(a.SolarDay.Sunrise)}   Untergang {Clock(a.SolarDay.Sunset)}");
        text.AppendLine($"  Bürgerl. Dämmerung   : {Clock(a.SolarDay.CivilDawn)} / {Clock(a.SolarDay.CivilDusk)}");
        text.AppendLine($"  Mond                 : {a.Moon.Name}, {a.Moon.IlluminatedPercent} % beleuchtet");

        text.AppendLine();
        text.AppendLine($"NOWCAST     : {a.Outlook.Summary}");

        if (a.Hints.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("EINSATZHINWEISE");
            foreach (TacticalHint hint in a.Hints)
            {
                string marker = hint.Severity switch
                {
                    HintSeverity.Warning => "[!]",
                    HintSeverity.Caution => "[o]",
                    _ => "[i]"
                };
                text.AppendLine($"  {marker} {hint.Topic}: {hint.Text}");
            }
        }

        text.AppendLine();
        text.AppendLine($"Quelle: {s.ModelName}; Warnungen: Deutscher Wetterdienst.");

        return text.ToString();
    }

    /// <summary>
    /// One-line summary to read out over the radio, kept short enough to speak in
    /// a single transmission.
    /// </summary>
    public static string BuildRadioLine(TacticalAssessment a)
    {
        WeatherSnapshot s = a.Snapshot;
        var parts = new List<string>();

        if (s.TemperatureC is { } temp)
        {
            parts.Add($"{temp.ToString("F0", German)} Grad");
        }

        if (s.WindSpeedMs is { } wind)
        {
            parts.Add($"Wind aus {a.WindFromCompass} mit {WindScale.MsToKmh(wind).ToString("F0", German)} Kilometer pro Stunde");
        }

        if (s.WindGustMs is { } gust && gust > (s.WindSpeedMs ?? 0) + 2.0)
        {
            parts.Add($"Böen {WindScale.MsToKmh(gust).ToString("F0", German)}");
        }

        parts.Add($"Ausbreitung nach {a.DownwindCompass}");
        parts.Add($"Ausbreitungsklasse {a.Stability.KlugManier}");

        if (a.WindShift is { } shift)
        {
            parts.Add($"Achtung Winddreher {shift.DirectionLabel} nach {WindScale.CompassPoint(shift.ToDeg)}");
        }

        if (s.RelativeHumidityPercent is { } humidity)
        {
            parts.Add($"Feuchte {humidity.ToString("F0", German)} Prozent");
        }

        return string.Join(", ", parts) + ".";
    }

    private static string Value(double? value, string format, string unit) =>
        value is null ? "—" : $"{value.Value.ToString(format, German)} {unit}";

    private static string Visibility(double? metres) => metres switch
    {
        null => "—",
        >= 10_000 => "> 10 km",
        >= 1_000 => $"{(metres.Value / 1000.0).ToString("F1", German)} km",
        _ => $"{metres.Value.ToString("F0", German)} m"
    };

    private static string Clock(DateTimeOffset? instant) =>
        instant?.ToLocalTime().ToString("HH:mm") ?? "—";
}
