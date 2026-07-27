using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ElwMeteo.Presentation.Charting;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Time;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>A legend entry, so identity is never carried by colour alone.</summary>
public sealed record LegendEntry(string Name, UiColour Swatch, string Detail);

/// <summary>One day of the outlook, for the table underneath the chart.</summary>
public sealed record DayRow(string Day, string Minimum, string Maximum, string Precipitation, string Uv, string Gust);

/// <summary>Tab 4 — how temperature and precipitation develop over the coming days.</summary>
public sealed partial class TrendViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    // Categorical slots 1-3 of the validated dark palette. Checked with the
    // data-viz validator against this application's panel surface (#171C24):
    // lightness band, chroma floor, all-pairs CVD separation, normal-vision
    // separation and contrast all pass. Do not re-tint these individually.
    private static readonly UiColour TemperatureColour = UiColour.FromRgb(0xD9, 0x59, 0x26); // orange
    private static readonly UiColour DewPointColour = UiColour.FromRgb(0x19, 0x9E, 0x70);    // aqua
    private static readonly UiColour PrecipitationColour = UiColour.FromRgb(0x39, 0x87, 0xE5); // blue

    [ObservableProperty]
    private TrendChartModel _chart = TrendChartModel.Empty;

    [ObservableProperty]
    private string _headline = "Noch keine Daten abgerufen.";

    [ObservableProperty]
    private string _range = string.Empty;

    [ObservableProperty]
    private string _extremes = string.Empty;

    public ObservableCollection<LegendEntry> Legend { get; } = [];

    public ObservableCollection<DayRow> Days { get; } = [];

    /// <summary>Rebuilds the chart from a fresh assessment.</summary>
    public void Apply(TacticalAssessment assessment, DateTimeOffset now)
    {
        WeatherSnapshot snapshot = assessment.Snapshot;

        var hourly = snapshot.Hourly
            .Where(h => h.Time >= now.AddHours(-6) && h.Time <= now.AddHours(48))
            .OrderBy(h => h.Time)
            .ToList();

        if (hourly.Count == 0)
        {
            Chart = TrendChartModel.Empty;
            Headline = "Keine Verlaufsdaten verfügbar.";
            return;
        }

        var temperature = hourly.Select(h => new TrendPoint(h.Time, h.TemperatureC)).ToList();
        var dewPoint = hourly
            .Select(h => new TrendPoint(h.Time, DewPointFor(h)))
            .ToList();

        var lines = new List<TrendSeries>
        {
            new("Temperatur", TemperatureColour, temperature),
            new("Taupunkt", DewPointColour, dewPoint, Dashed: true)
        };

        var bars = new TrendBars(
            "Niederschlag",
            PrecipitationColour,
            hourly.Select(h => new TrendPoint(h.Time, h.PrecipitationMm)).ToList(),
            "mm/h");

        Chart = new TrendChartModel(
            lines,
            bars,
            BuildNightSpans(assessment, hourly.First().Time, hourly.Last().Time),
            now,
            "°C");

        BuildLegend(assessment, hourly);
        BuildSummary(hourly, now);
        BuildDays(snapshot);
    }

    /// <summary>
    /// Dew point per hour. The provider gives it only for the current conditions,
    /// so the hourly curve is derived from temperature and humidity.
    /// </summary>
    private static double? DewPointFor(HourlyStep step)
    {
        if (step.TemperatureC is not { } temperature || step.RelativeHumidityPercent is not { } humidity)
        {
            return null;
        }

        return Core.Meteorology.Thermodynamics.DewPointC(temperature, humidity);
    }

    /// <summary>Shaded bands between sunset and sunrise, drawn behind the curves.</summary>
    private static List<(DateTimeOffset From, DateTimeOffset To)> BuildNightSpans(
        TacticalAssessment assessment,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var spans = new List<(DateTimeOffset, DateTimeOffset)>();
        GeoPosition position = assessment.Snapshot.Position;

        for (DateTimeOffset day = from.Date.AddDays(-1); day <= to.Date.AddDays(1); day = day.AddDays(1))
        {
            SolarDay solar = SolarCalculator.CalculateDay(position.Latitude, position.Longitude, day);
            SolarDay next = SolarCalculator.CalculateDay(position.Latitude, position.Longitude, day.AddDays(1));

            if (solar.Sunset is { } sunset && next.Sunrise is { } sunrise)
            {
                spans.Add((sunset, sunrise));
            }
        }

        return spans;
    }

    private void BuildLegend(TacticalAssessment assessment, List<HourlyStep> hourly)
    {
        Legend.Clear();

        Legend.Add(new LegendEntry(
            "Temperatur",
            TemperatureColour,
            $"aktuell {Format(assessment.Snapshot.TemperatureC, "°C")}"));

        Legend.Add(new LegendEntry(
            "Taupunkt",
            DewPointColour,
            $"aktuell {Format(assessment.Snapshot.DewPointC, "°C")}"));

        double total = hourly.Sum(h => h.PrecipitationMm ?? 0);
        Legend.Add(new LegendEntry(
            "Niederschlag",
            PrecipitationColour,
            $"Summe {total.ToString("F1", German)} mm im Zeitraum"));
    }

    private void BuildSummary(List<HourlyStep> hourly, DateTimeOffset now)
    {
        var ahead = hourly.Where(h => h.Time >= now).ToList();
        if (ahead.Count == 0)
        {
            return;
        }

        var withTemperature = ahead.Where(h => h.TemperatureC is not null).ToList();
        if (withTemperature.Count == 0)
        {
            return;
        }

        HourlyStep coldest = withTemperature.MinBy(h => h.TemperatureC!.Value)!;
        HourlyStep warmest = withTemperature.MaxBy(h => h.TemperatureC!.Value)!;

        Range = $"{hourly.First().Time.ToLocalTime():dd.MM. HH:mm} – {hourly.Last().Time.ToLocalTime():dd.MM. HH:mm}";

        Extremes =
            $"Tiefstwert {Format(coldest.TemperatureC, "°C")} um {coldest.Time.ToLocalTime():HH:mm} Uhr · " +
            $"Höchstwert {Format(warmest.TemperatureC, "°C")} um {warmest.Time.ToLocalTime():HH:mm} Uhr";

        // Frost is the single fact from this panel that changes what gets ordered.
        bool frost = withTemperature.Any(h => h.TemperatureC <= 0);
        Headline = frost
            ? "ACHTUNG: Frost im Vorhersagezeitraum — Löschwasser gefriert, Streumittel und Absicherung einplanen."
            : $"Temperaturverlauf über {(int)(hourly.Last().Time - hourly.First().Time).TotalHours} Stunden.";
    }

    private void BuildDays(WeatherSnapshot snapshot)
    {
        Days.Clear();

        foreach (DailySummary day in snapshot.Daily.Take(4))
        {
            Days.Add(new DayRow(
                day.Date.ToLocalTime().ToString("ddd, dd.MM.", German),
                Format(day.TemperatureMinC, "°C"),
                Format(day.TemperatureMaxC, "°C"),
                Format(day.PrecipitationSumMm, "mm"),
                day.UvIndexMax is { } uv ? uv.ToString("F0", German) : "—",
                day.WindGustMaxMs is { } gust
                    ? $"{Core.Meteorology.WindScale.MsToKmh(gust).ToString("F0", German)} km/h"
                    : "—"));
        }
    }

    private static string Format(double? value, string unit) =>
        value is null || double.IsNaN(value.Value) ? "—" : $"{value.Value.ToString("F1", German)} {unit}";
}
