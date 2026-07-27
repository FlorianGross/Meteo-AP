using System.Globalization;
using System.Text;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Time;

namespace ElwMeteo.Core.Reporting;

/// <summary>
/// Appends each refresh to a semicolon-separated CSV, one file per day.
/// Semicolons and comma decimal separators keep German Excel happy without an
/// import wizard, which matters when the log has to go into the incident report.
/// </summary>
public sealed class SnapshotCsvLogger(string directory)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private const string HeaderLine =
        "Zeitpunkt (Ortszeit);Taktische Zeit;Breite;Länge;Quelle;Temperatur °C;Gefühlt °C;" +
        "Taupunkt °C;Feuchte %;Druck hPa;Wind m/s;Wind km/h;Böen m/s;Richtung °;Richtung;" +
        "Ausbreitung °;Bewölkung %;Sicht m;Niederschlag mm/h;CAPE J/kg;WMO-Code;" +
        "Ausbreitungsklasse;Pasquill;WBGT °C;Ångström";

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Directory { get; } = directory;

    public string FilePathFor(DateTimeOffset instant) =>
        Path.Combine(Directory, $"elw-meteo-log-{instant.ToLocalTime():yyyy-MM-dd}.csv");

    public async Task AppendAsync(TacticalAssessment assessment, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        string path = FilePathFor(now);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            bool isNewFile = !File.Exists(path);
            var line = new StringBuilder();

            if (isNewFile)
            {
                line.AppendLine(HeaderLine);
            }

            line.AppendLine(BuildRow(assessment, now));

            // UTF-8 with BOM so Excel picks up the umlauts in the header.
            await File.AppendAllTextAsync(path, line.ToString(), new UTF8Encoding(true), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    internal static string BuildRow(TacticalAssessment a, DateTimeOffset now)
    {
        WeatherSnapshot s = a.Snapshot;

        var fields = new[]
        {
            now.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", German),
            TacticalTime.FormatLocal(now),
            Num(s.Position.Latitude, "F5"),
            Num(s.Position.Longitude, "F5"),
            s.Position.SourceLabel,
            Num(s.TemperatureC, "F1"),
            Num(s.ApparentTemperatureC, "F1"),
            Num(s.DewPointC, "F1"),
            Num(s.RelativeHumidityPercent, "F0"),
            Num(s.PressureMslHpa, "F1"),
            Num(s.WindSpeedMs, "F1"),
            Num(s.WindSpeedMs is null ? null : WindScale.MsToKmh(s.WindSpeedMs.Value), "F0"),
            Num(s.WindGustMs, "F1"),
            Num(s.WindDirectionDeg, "F0"),
            a.WindFromCompass,
            Num(a.DownwindBearingDeg, "F0"),
            Num(s.CloudCoverPercent, "F0"),
            Num(s.VisibilityM, "F0"),
            Num(s.PrecipitationMm, "F1"),
            Num(s.CapeJkg, "F0"),
            s.WeatherCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            a.Stability.KlugManier,
            a.Stability.Pasquill.ToString(),
            Num(a.WbgtShadeC, "F1"),
            Num(a.FireRisk.AngstromIndex, "F2")
        };

        return string.Join(';', fields.Select(Escape));
    }

    private static string Num(double? value, string format) =>
        value is null || double.IsNaN(value.Value) ? string.Empty : value.Value.ToString(format, German);

    /// <summary>Quotes a field when it contains a separator, quote or newline.</summary>
    private static string Escape(string field)
    {
        if (field.IndexOfAny([';', '"', '\n', '\r']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
