using System.Globalization;
using System.Text;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Time;

namespace ElwMeteo.Core.Reporting;

/// <summary>What goes on the report besides the weather itself.</summary>
public sealed record ReportOptions
{
    /// <summary>Free text identifying the operation, printed in the header.</summary>
    public string? IncidentLabel { get; init; }

    /// <summary>Reverse-geocoded address of the position.</summary>
    public string? AddressLine { get; init; }

    public IReadOnlyList<DwdWarning> Warnings { get; init; } = [];

    public string? WarningSource { get; init; }

    /// <summary>Set when the report is built from the stored state rather than a live fetch.</summary>
    public TimeSpan? OfflineAge { get; init; }

    /// <summary>Name of the unit, printed in the footer.</summary>
    public string? Organisation { get; init; }
}

/// <summary>
/// Renders a complete weather report as a self-contained HTML page, laid out for
/// A4 and ready to print or save as PDF.
///
/// HTML into the system browser rather than a PDF library, for three reasons
/// that all point the same way. The browser's own print dialogue can already
/// save as PDF on Windows, macOS and Linux, so the feature works identically on
/// all three heads without a dependency that has to be licensed and shipped.
/// The map tab already uses exactly this route, so there is one pattern to
/// understand rather than two. And a page of HTML is a string, which means the
/// whole thing is testable in the domain library — a printed report that comes
/// out wrong is not something anybody notices until it is on paper.
///
/// Everything that came off the network — addresses, warning texts from NINA and
/// the DWD — goes through <see cref="Escape"/>. A warning description is
/// attacker-adjacent input in a way a temperature is not.
/// </summary>
public static class WeatherReportPage
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Build(TacticalAssessment assessment, DateTimeOffset now, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        WeatherSnapshot snapshot = assessment.Snapshot;

        var html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"de\"><head><meta charset=\"utf-8\" />");
        html.AppendLine($"<title>Wetterbericht {Escape(now.ToString("dd.MM.yyyy HH:mm", German))}</title>");
        html.AppendLine($"<style>{Styles}</style>");
        html.AppendLine("</head><body>");

        AppendHeader(html, assessment, now, options);
        AppendOfflineBanner(html, options);
        AppendWarnings(html, options);
        AppendCurrentConditions(html, assessment);
        AppendDispersion(html, assessment, now);
        AppendChart(html, snapshot, now);
        AppendDays(html, snapshot);
        AppendAstronomy(html, assessment);
        AppendHints(html, assessment);
        AppendFooter(html, snapshot, options);

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    // -------------------------------------------------------------- header

    private static void AppendHeader(
        StringBuilder html, TacticalAssessment assessment, DateTimeOffset now, ReportOptions options)
    {
        WeatherSnapshot snapshot = assessment.Snapshot;

        html.AppendLine("<header class=\"sheet-head\">");
        html.AppendLine("<div class=\"title-block\">");
        html.AppendLine("<h1>Wetterbericht</h1>");

        if (!string.IsNullOrWhiteSpace(options.IncidentLabel))
        {
            html.AppendLine($"<p class=\"incident\">{Escape(options.IncidentLabel)}</p>");
        }

        html.AppendLine("</div>");
        html.AppendLine("<div class=\"stamp\">");
        html.AppendLine($"<div class=\"stamp-time\">{Escape(TacticalTime.FormatLocal(now))}</div>");
        html.AppendLine($"<div>{Escape(now.ToLocalTime().ToString("dddd, dd. MMMM yyyy, HH:mm", German))} Uhr</div>");
        html.AppendLine("</div>");
        html.AppendLine("</header>");

        html.AppendLine("<section class=\"position\">");
        html.AppendLine("<table class=\"kv\">");
        Row(html, "Position", Escape(Geodesy.FormatDegreesDecimalMinutes(snapshot.Position.ToLatLon())));
        Row(html, "Dezimal",
            $"{snapshot.Position.Latitude.ToString("F5", German)} / " +
            $"{snapshot.Position.Longitude.ToString("F5", German)} " +
            $"<span class=\"muted\">({Escape(snapshot.Position.SourceLabel)})</span>");

        if (!string.IsNullOrWhiteSpace(options.AddressLine))
        {
            Row(html, "Anschrift", Escape(options.AddressLine));
        }

        if (snapshot.ElevationM is { } elevation)
        {
            Row(html, "Höhe", $"{elevation.ToString("F0", German)} m ü. NN");
        }

        html.AppendLine("</table>");
        html.AppendLine("</section>");
    }

    private static void AppendOfflineBanner(StringBuilder html, ReportOptions options)
    {
        if (options.OfflineAge is not { } age)
        {
            return;
        }

        html.AppendLine("<p class=\"offline\">Dieser Bericht wurde aus dem <strong>gespeicherten Stand</strong> " +
                        $"erstellt — die Daten sind {Escape(DescribeAge(age))} abgerufen worden und beschreiben " +
                        "nicht zwingend die Lage zum Druckzeitpunkt.</p>");
    }

    // ------------------------------------------------------------ warnings

    private static void AppendWarnings(StringBuilder html, ReportOptions options)
    {
        if (options.Warnings.Count == 0)
        {
            return;
        }

        html.AppendLine("<section class=\"block warnings\">");
        html.AppendLine($"<h2>Amtliche Warnungen <span class=\"count\">{options.Warnings.Count}</span></h2>");

        foreach (DwdWarning warning in options.Warnings)
        {
            html.AppendLine($"<article class=\"warning\" style=\"border-left-color:{Escape(warning.LevelColour)}\">");
            html.AppendLine($"<h3>{Escape(warning.Headline)}</h3>");
            html.AppendLine("<p class=\"warning-meta\">" +
                            $"{Escape(warning.LevelLabel)} · {Escape(warning.TimeRangeLabel)} · " +
                            $"{Escape(warning.RegionName)}</p>");

            if (!string.IsNullOrWhiteSpace(warning.Description))
            {
                html.AppendLine($"<p>{Escape(warning.Description)}</p>");
            }

            if (!string.IsNullOrWhiteSpace(warning.Instruction))
            {
                html.AppendLine($"<p class=\"instruction\">{Escape(warning.Instruction)}</p>");
            }

            html.AppendLine("</article>");
        }

        if (!string.IsNullOrWhiteSpace(options.WarningSource))
        {
            html.AppendLine($"<p class=\"source\">Quelle: {Escape(options.WarningSource)}</p>");
        }

        html.AppendLine("</section>");
    }

    // ---------------------------------------------------- current weather

    private static void AppendCurrentConditions(StringBuilder html, TacticalAssessment assessment)
    {
        WeatherSnapshot s = assessment.Snapshot;

        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Aktuelle Lage</h2>");
        html.AppendLine("<div class=\"columns\">");

        html.AppendLine("<table class=\"kv\">");
        Row(html, "Wetter", Escape(WeatherCodes.Describe(s.WeatherCode)));
        Row(html, "Temperatur", $"<strong>{Value(s.TemperatureC, "F1", "°C")}</strong> " +
                                $"<span class=\"muted\">gefühlt {Value(s.ApparentTemperatureC, "F1", "°C")}</span>");
        Row(html, "Taupunkt", Value(s.DewPointC, "F1", "°C"));
        Row(html, "Luftfeuchte", Value(s.RelativeHumidityPercent, "F0", "%"));
        Row(html, "Luftdruck", Value(s.PressureMslHpa, "F0", "hPa (NN)"));
        html.AppendLine("</table>");

        html.AppendLine("<table class=\"kv\">");
        Row(html, "Wind",
            $"aus <strong>{Escape(assessment.WindFromCompass)}</strong> ({Value(s.WindDirectionDeg, "F0", "°")}), " +
            $"{Value(s.WindSpeedMs is null ? null : WindScale.MsToKmh(s.WindSpeedMs.Value), "F0", "km/h")}");
        Row(html, "Windstärke",
            $"{assessment.Beaufort.Force} Bft — {Escape(assessment.Beaufort.Description)}");
        Row(html, "Böen",
            Value(s.WindGustMs is null ? null : WindScale.MsToKmh(s.WindGustMs.Value), "F0", "km/h"));
        Row(html, "Bewölkung", Value(s.CloudCoverPercent, "F0", "%"));
        Row(html, "Sicht", Visibility(s.VisibilityM));
        html.AppendLine("</table>");

        html.AppendLine("</div>");
        html.AppendLine("</section>");
    }

    private static void AppendDispersion(StringBuilder html, TacticalAssessment a, DateTimeOffset now)
    {
        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Ausbreitung und Windentwicklung</h2>");
        html.AppendLine("<table class=\"kv wide\">");

        Row(html, "Ausbreitungsrichtung",
            $"nach <strong>{Escape(a.DownwindCompass)}</strong> " +
            $"({a.DownwindBearingDeg.ToString("F0", German)}°)");
        Row(html, "Ausbreitungsklasse",
            $"{Escape(a.Stability.KlugManier)} / Pasquill {Escape(a.Stability.Pasquill.ToString())} " +
            $"<span class=\"muted\">({Escape(a.Stability.Label)})</span>");
        Row(html, "Hinweis", Escape(a.Stability.TacticalNote));
        Row(html, "Winddreher",
            Escape(a.WindShift is { } shift ? shift.Describe(now) : "keine relevante Drehung erwartet"));
        Row(html, "Böenspitze", Escape(a.GustPeak is { } peak ? peak.Describe(now) : "—"));
        Row(html, "Niederschlag", Escape(a.Outlook.Summary));

        html.AppendLine("</table>");
        html.AppendLine("</section>");
    }

    // --------------------------------------------------------------- chart

    private static void AppendChart(StringBuilder html, WeatherSnapshot snapshot, DateTimeOffset now)
    {
        var window = snapshot.Hourly
            .Where(h => h.Time >= now.AddHours(-6) && h.Time <= now.AddHours(48))
            .ToList();

        string svg = ReportChart.Build(window, now);

        if (svg.Length == 0)
        {
            return;
        }

        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Verlauf der nächsten 48 Stunden</h2>");
        html.AppendLine(svg);
        html.AppendLine("<p class=\"legend\">" +
                        "<span class=\"key temperature\"></span> Temperatur &nbsp; " +
                        "<span class=\"key dewpoint\"></span> Taupunkt &nbsp; " +
                        "<span class=\"key rain\"></span> Niederschlag &nbsp; " +
                        "<span class=\"key night\"></span> Nacht</p>");
        html.AppendLine("</section>");
    }

    private static void AppendDays(StringBuilder html, WeatherSnapshot snapshot)
    {
        var days = snapshot.Daily.Take(5).ToList();

        if (days.Count == 0)
        {
            return;
        }

        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Tagesübersicht</h2>");
        html.AppendLine("<table class=\"grid\">");
        html.AppendLine("<thead><tr><th>Tag</th><th>Tief</th><th>Hoch</th>" +
                        "<th>Niederschlag</th><th>Böen</th><th>UV</th></tr></thead><tbody>");

        foreach (DailySummary day in days)
        {
            html.AppendLine("<tr>" +
                $"<td>{Escape(day.Date.ToLocalTime().ToString("ddd, dd.MM.", German))}</td>" +
                $"<td>{Value(day.TemperatureMinC, "F1", "°C")}</td>" +
                $"<td>{Value(day.TemperatureMaxC, "F1", "°C")}</td>" +
                $"<td>{Value(day.PrecipitationSumMm, "F1", "mm")}</td>" +
                $"<td>{Value(day.WindGustMaxMs is null ? null : WindScale.MsToKmh(day.WindGustMaxMs.Value), "F0", "km/h")}</td>" +
                $"<td>{Value(day.UvIndexMax, "F0", "")}</td>" +
                "</tr>");
        }

        html.AppendLine("</tbody></table>");
        html.AppendLine("</section>");
    }

    private static void AppendAstronomy(StringBuilder html, TacticalAssessment a)
    {
        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Sonne und Mond</h2>");
        html.AppendLine("<table class=\"kv wide\">");

        Row(html, "Sonnenaufgang", Time(a.SolarDay.Sunrise));
        Row(html, "Sonnenuntergang", Time(a.SolarDay.Sunset));
        Row(html, "Dämmerungsende", Time(a.SolarDay.CivilDusk));
        Row(html, "Mondphase", $"{Escape(a.Moon.Name)} " +
                               $"<span class=\"muted\">({a.Moon.IlluminatedPercent.ToString(German)} % beleuchtet)</span>");

        html.AppendLine("</table>");
        html.AppendLine("</section>");
    }

    private static void AppendHints(StringBuilder html, TacticalAssessment a)
    {
        if (a.Hints.Count == 0)
        {
            return;
        }

        html.AppendLine("<section class=\"block\">");
        html.AppendLine("<h2>Einsatzhinweise</h2>");
        html.AppendLine("<ul class=\"hints\">");

        foreach (TacticalHint hint in a.Hints)
        {
            string severity = hint.Severity.ToString().ToLowerInvariant();
            html.AppendLine($"<li class=\"{Escape(severity)}\"><strong>{Escape(hint.Topic)}:</strong> " +
                            $"{Escape(hint.Text)}</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("</section>");
    }

    private static void AppendFooter(StringBuilder html, WeatherSnapshot snapshot, ReportOptions options)
    {
        html.AppendLine("<footer class=\"sheet-foot\">");

        if (!string.IsNullOrWhiteSpace(options.Organisation))
        {
            html.AppendLine($"<div>{Escape(options.Organisation)}</div>");
        }

        html.AppendLine($"<div>Modell: {Escape(snapshot.ModelName)} · " +
                        $"Datenstand {Escape(snapshot.RetrievedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German))} Uhr</div>");
        html.AppendLine("<div>Quellen: Deutscher Wetterdienst · Open-Meteo · Bright Sky · " +
                        "Bundesamt für Bevölkerungsschutz und Katastrophenhilfe (NINA)</div>");
        html.AppendLine("<div class=\"muted\">Erstellt mit ELW-Meteo. Amtliche Warnungen und Anordnungen " +
                        "der zuständigen Stellen haben Vorrang vor dieser Auswertung.</div>");
        html.AppendLine("</footer>");
    }

    // ------------------------------------------------------------- helpers

    private static void Row(StringBuilder html, string label, string value) =>
        html.AppendLine($"<tr><th>{Escape(label)}</th><td>{value}</td></tr>");

    private static string Value(double? value, string format, string unit)
    {
        if (value is null || double.IsNaN(value.Value))
        {
            return "<span class=\"muted\">—</span>";
        }

        string text = value.Value.ToString(format, German);
        return unit.Length == 0 ? Escape(text) : $"{Escape(text)} {Escape(unit)}";
    }

    private static string Visibility(double? metres) => metres switch
    {
        null => "<span class=\"muted\">—</span>",
        < 1000 => $"{metres.Value.ToString("F0", German)} m",
        _ => $"{(metres.Value / 1000).ToString("F1", German)} km"
    };

    private static string Time(DateTimeOffset? value) =>
        value is null ? "<span class=\"muted\">—</span>" : $"{value.Value.ToLocalTime().ToString("HH:mm", German)} Uhr";

    internal static string DescribeAge(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "gerade eben",
        < 60 => $"vor {(int)age.TotalMinutes} Minuten",
        _ => $"vor {(int)age.TotalHours} Stunden {age.Minutes} Minuten"
    };

    /// <summary>
    /// HTML-escapes a value. Applied to everything that did not originate as a
    /// number in this process — warning texts and addresses come off the
    /// network, and this page is opened in a real browser.
    /// </summary>
    internal static string Escape(string? value) => (value ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    // --------------------------------------------------------------- style
    //
    // Black on white, not the application's dark theme: this goes on paper, and
    // a dark panel printed out is both unreadable and a cartridge's worth of
    // toner. Sections are kept off page breaks so a warning never splits across
    // two sheets.

    private const string Styles = """
        @page { size: A4 portrait; margin: 14mm 12mm 12mm 12mm; }

        * { box-sizing: border-box; }

        body {
          font-family: "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
          font-size: 10.5pt; line-height: 1.35; color: #111; background: #fff;
          margin: 0 auto; padding: 10mm; max-width: 210mm;
        }

        h1 { font-size: 21pt; margin: 0; letter-spacing: 0.5px; }
        h2 {
          font-size: 11pt; text-transform: uppercase; letter-spacing: 1.2px;
          margin: 0 0 5px; padding-bottom: 3px; border-bottom: 1.5px solid #111;
        }
        h3 { font-size: 11pt; margin: 0 0 2px; }

        .sheet-head {
          display: flex; justify-content: space-between; align-items: flex-start;
          border-bottom: 3px solid #111; padding-bottom: 7px; margin-bottom: 9px;
        }
        .incident { margin: 3px 0 0; font-size: 12pt; font-weight: 600; }
        .stamp { text-align: right; font-size: 9.5pt; }
        .stamp-time { font-family: "Consolas", "DejaVu Sans Mono", monospace;
                      font-size: 13pt; font-weight: 700; }

        .block { margin-top: 11px; page-break-inside: avoid; }
        .columns { display: flex; gap: 14px; }
        .columns > table { flex: 1; }

        table { border-collapse: collapse; width: 100%; }
        .kv th {
          text-align: left; font-weight: 600; color: #444; width: 40%;
          padding: 2.5px 8px 2.5px 0; vertical-align: top; font-size: 9.5pt;
        }
        .kv.wide th { width: 26%; }
        .kv td { padding: 2.5px 0; vertical-align: top; }

        .grid th, .grid td {
          border: 1px solid #bbb; padding: 4px 7px; text-align: right; font-size: 9.5pt;
        }
        .grid th { background: #eee; font-weight: 600; }
        .grid td:first-child, .grid th:first-child { text-align: left; }

        .position { border-bottom: 1px solid #ccc; padding-bottom: 7px; }
        .muted { color: #666; }

        .offline {
          border: 2px solid #b35c00; background: #fff4e5; padding: 7px 9px;
          margin: 10px 0 0; font-size: 10pt;
        }

        .warnings .count {
          font-size: 9pt; background: #111; color: #fff;
          padding: 1px 6px; border-radius: 8px; vertical-align: middle;
        }
        .warning {
          border-left: 5px solid #999; background: #f7f7f7;
          padding: 6px 9px; margin-top: 6px; page-break-inside: avoid;
        }
        .warning p { margin: 3px 0 0; }
        .warning-meta { font-size: 9pt; color: #555; }
        .instruction { font-weight: 600; }
        .source { font-size: 8.5pt; color: #666; margin: 5px 0 0; }

        .hints { margin: 4px 0 0; padding-left: 17px; }
        .hints li { margin-bottom: 2.5px; }
        .hints li.warning, .hints li.critical { font-weight: 600; }

        .chart { width: 100%; height: auto; margin-top: 4px; }
        .chart .grid { stroke: #d0d0d0; stroke-width: 1; }
        .chart .freeze { stroke: #3987e5; stroke-width: 1; stroke-dasharray: 2 3; }
        .chart .night { fill: #ecedf1; }
        .chart .rain { fill: #7fb3e8; }
        .chart .temperature { fill: none; stroke: #c1440e; stroke-width: 2.2; }
        .chart .dewpoint { fill: none; stroke: #17795e; stroke-width: 1.6; stroke-dasharray: 6 3; }
        .chart .now { stroke: #111; stroke-width: 1.2; stroke-dasharray: 3 2; }
        .chart .axis { font-size: 10px; fill: #555; font-family: sans-serif; }
        .chart .end { text-anchor: end; }
        .chart .middle { text-anchor: middle; }
        .chart .faint { fill: #999; }

        .legend { font-size: 9pt; color: #444; margin: 4px 0 0; }
        .key { display: inline-block; width: 15px; height: 8px; vertical-align: middle; margin-right: 3px; }
        .key.temperature { background: #c1440e; }
        .key.dewpoint { background: #17795e; }
        .key.rain { background: #7fb3e8; }
        .key.night { background: #ecedf1; border: 1px solid #ccc; }

        .sheet-foot {
          margin-top: 14px; padding-top: 6px; border-top: 1px solid #999;
          font-size: 8.5pt; color: #555;
        }

        @media print {
          body { padding: 0; max-width: none; }
          .block { break-inside: avoid; }
        }
        """;
}
