using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Reporting;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WeatherAssessorTests
{
    private static readonly DateTimeOffset SummerNoon = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly GeoPosition Frankfurt =
        new(50.1109, 8.6821, PositionSource.Manual, SummerNoon);

    private static WeatherSnapshot Snapshot(
        double? temperature = 22.0,
        double? humidity = 55.0,
        double? wind = 4.0,
        double? gust = 6.0,
        double? direction = 270.0,
        double? cloud = 30.0,
        double? visibility = 20000.0,
        double? cape = 100.0,
        int? weatherCode = 1,
        IReadOnlyList<NowcastStep>? nowcast = null) =>
        new()
        {
            Position = Frankfurt,
            RetrievedAtUtc = SummerNoon,
            TemperatureC = temperature,
            RelativeHumidityPercent = humidity,
            WindSpeedMs = wind,
            WindGustMs = gust,
            WindDirectionDeg = direction,
            CloudCoverPercent = cloud,
            VisibilityM = visibility,
            CapeJkg = cape,
            WeatherCode = weatherCode,
            PressureMslHpa = 1013.0,
            Nowcast = nowcast ?? []
        };

    [Fact]
    public void Assess_DerivesWindAndDownwindDirection()
    {
        var result = WeatherAssessor.Assess(Snapshot(direction: 270.0), SummerNoon);

        Assert.Equal("W", result.WindFromCompass);
        Assert.Equal(90.0, result.DownwindBearingDeg, 1e-9);
        Assert.Equal("O", result.DownwindCompass);
    }

    [Fact]
    public void Assess_FillsInDewPointWhenTheProviderOmitsIt()
    {
        var result = WeatherAssessor.Assess(Snapshot(temperature: 20.0, humidity: 50.0), SummerNoon);

        Assert.NotNull(result.ConvectiveCloudBaseM);
        // T-Td spread of ~10.7 K puts the base near 1340 m.
        Assert.InRange(result.ConvectiveCloudBaseM!.Value, 1200.0, 1450.0);
    }

    [Fact]
    public void Assess_ToleratesACompletelyEmptySnapshot()
    {
        var snapshot = new WeatherSnapshot { Position = Frankfurt, RetrievedAtUtc = SummerNoon };
        var result = WeatherAssessor.Assess(snapshot, SummerNoon);

        Assert.NotNull(result.Stability);
        Assert.NotNull(result.Beaufort);
        Assert.Null(result.WbgtShadeC);
        Assert.Null(result.WindChillC);
    }

    [Fact]
    public void Assess_WarnsAboutGustsAtTheAerialLadderLimit()
    {
        var result = WeatherAssessor.Assess(Snapshot(wind: 10.0, gust: 14.0), SummerNoon);

        Assert.Contains(result.Hints, h => h.Topic == "Drehleiter");
    }

    [Fact]
    public void Assess_DoesNotWarnAboutGustsInLightWind()
    {
        var result = WeatherAssessor.Assess(Snapshot(wind: 3.0, gust: 5.0), SummerNoon);

        Assert.DoesNotContain(result.Hints, h => h.Topic == "Drehleiter");
    }

    [Fact]
    public void Assess_WarnsAboutCalmAirHavingNoDispersionDirection()
    {
        var result = WeatherAssessor.Assess(Snapshot(wind: 0.2, gust: 0.4), SummerNoon);

        Assert.Contains(result.Hints, h => h.Topic == "Windstille");
    }

    [Fact]
    public void Assess_WarnsAboutIcingNearFreezing()
    {
        var result = WeatherAssessor.Assess(Snapshot(temperature: -1.0, humidity: 80.0), SummerNoon);

        var hint = Assert.Single(result.Hints, h => h.Topic == "Glättegefahr");
        Assert.Equal(HintSeverity.Warning, hint.Severity);
    }

    [Fact]
    public void Assess_WarnsAboutHeatLoadUnderBreathingApparatus()
    {
        var result = WeatherAssessor.Assess(Snapshot(temperature: 34.0, humidity: 60.0), SummerNoon);

        var hint = Assert.Single(result.Hints, h => h.Topic == "Hitzebelastung");
        Assert.Equal(HintSeverity.Warning, hint.Severity);
    }

    [Fact]
    public void Assess_WarnsAboutReducedVisibility()
    {
        var result = WeatherAssessor.Assess(Snapshot(visibility: 150.0), SummerNoon);

        var hint = Assert.Single(result.Hints, h => h.Topic == "Sichtweite");
        Assert.Equal(HintSeverity.Warning, hint.Severity);
    }

    [Fact]
    public void Assess_WarnsAboutHighThunderstormPotential()
    {
        var result = WeatherAssessor.Assess(Snapshot(cape: 2000.0), SummerNoon);

        Assert.Contains(result.Hints, h => h.Topic == "Gewitter" && h.Severity == HintSeverity.Warning);
    }

    [Fact]
    public void Assess_WarnsAboutLightningPotentialFromTheNowcast()
    {
        var nowcast = new List<NowcastStep>
        {
            new(SummerNoon, 0.0, 0.0, 0.0, 4.0, 6.0, 270, 22, 100, 3.5, 95)
        };

        var result = WeatherAssessor.Assess(Snapshot(nowcast: nowcast), SummerNoon);

        Assert.Contains(result.Hints, h => h.Topic == "Blitzschlag");
    }

    [Fact]
    public void Assess_WarnsAboutStableStratificationForHazardousMaterials()
    {
        // Clear, calm night: stability class F.
        var night = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var result = WeatherAssessor.Assess(Snapshot(wind: 1.0, cloud: 0.0), night);

        Assert.Equal(PasquillClass.F, result.Stability.Pasquill);
        Assert.Contains(result.Hints, h => h.Topic == "Gefahrstoff" && h.Severity == HintSeverity.Warning);
    }

    [Fact]
    public void Assess_OrdersHintsWithTheMostSevereFirst()
    {
        var result = WeatherAssessor.Assess(
            Snapshot(temperature: -6.0, humidity: 90.0, wind: 15.0, gust: 25.0, visibility: 100.0),
            SummerNoon);

        Assert.True(result.Hints.Count > 1);
        Assert.Equal(HintSeverity.Warning, result.Hints[0].Severity);
        Assert.Equal(HintSeverity.Warning, result.WorstSeverity);
    }

    [Fact]
    public void Assess_ProducesNoHintsInBenignConditions()
    {
        // Mild, moderate wind, good visibility, no convective energy.
        var result = WeatherAssessor.Assess(
            Snapshot(temperature: 16.0, humidity: 65.0, wind: 4.0, gust: 6.0, cape: 0.0, weatherCode: 1),
            SummerNoon);

        Assert.Empty(result.Hints);
        Assert.Equal(HintSeverity.Info, result.WorstSeverity);
    }
}

public class NowcastOutlookTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static NowcastStep Step(int minutesFromNow, double precipitation) =>
        new(Now.AddMinutes(minutesFromNow), precipitation, precipitation, 0, 4, 6, 270, 18, 100, 0, 61);

    [Fact]
    public void BuildOutlook_ReportsWhenRainStarts()
    {
        var steps = new List<NowcastStep> { Step(0, 0), Step(15, 0), Step(30, 0.4), Step(45, 0.8) };

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.False(outlook.RainingNow);
        Assert.Equal(TimeSpan.FromMinutes(30), outlook.StartsIn);
        Assert.Null(outlook.StopsIn);
        Assert.Contains("beginnt in ca. 30 min", outlook.Summary);
    }

    [Fact]
    public void BuildOutlook_ReportsWhenRainStops()
    {
        var steps = new List<NowcastStep> { Step(0, 1.2), Step(15, 0.6), Step(30, 0), Step(45, 0) };

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.True(outlook.RainingNow);
        Assert.Equal(TimeSpan.FromMinutes(30), outlook.StopsIn);
        Assert.Contains("lässt in ca. 30 min nach", outlook.Summary);
    }

    [Fact]
    public void BuildOutlook_ConvertsQuarterHourTotalsToHourlyIntensity()
    {
        // 2.5 mm in 15 minutes is 10 mm/h.
        var steps = new List<NowcastStep> { Step(0, 0.5), Step(15, 2.5) };

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.Equal(10.0, outlook.PeakIntensityMmPerHour, 1e-9);
    }

    [Fact]
    public void BuildOutlook_ReportsPersistentRain()
    {
        var steps = Enumerable.Range(0, 12).Select(i => Step(i * 15, 0.5)).ToList();

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.True(outlook.RainingNow);
        Assert.Null(outlook.StopsIn);
        Assert.Contains("Anhaltender Niederschlag", outlook.Summary);
    }

    [Fact]
    public void BuildOutlook_ReportsADryPeriod()
    {
        var steps = Enumerable.Range(0, 12).Select(i => Step(i * 15, 0.0)).ToList();

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.False(outlook.RainingNow);
        Assert.Null(outlook.StartsIn);
        Assert.Contains("kein Niederschlag", outlook.Summary);
    }

    [Fact]
    public void BuildOutlook_IgnoresStepsThatAreAlreadyHistory()
    {
        var steps = new List<NowcastStep> { Step(-60, 5.0), Step(0, 0.0), Step(15, 0.0) };

        var outlook = WeatherAssessor.BuildOutlook(steps, Now);

        Assert.False(outlook.RainingNow);
        Assert.Equal(0.0, outlook.PeakIntensityMmPerHour);
    }

    [Fact]
    public void BuildOutlook_FallsBackWhenNoDataIsAvailable()
    {
        var outlook = WeatherAssessor.BuildOutlook([], Now);

        Assert.Contains("Kein Nowcast", outlook.Summary);
    }
}

public class ReportFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static TacticalAssessment BuildAssessment() =>
        WeatherAssessor.Assess(new WeatherSnapshot
        {
            Position = new GeoPosition(50.1109, 8.6821, PositionSource.Gps, Now, AccuracyM: 4),
            RetrievedAtUtc = Now,
            TemperatureC = 24.5,
            ApparentTemperatureC = 25.1,
            RelativeHumidityPercent = 58,
            DewPointC = 15.6,
            WindSpeedMs = 5.2,
            WindGustMs = 9.4,
            WindDirectionDeg = 225,
            CloudCoverPercent = 40,
            PressureMslHpa = 1016,
            VisibilityM = 24000,
            PrecipitationMm = 0,
            WeatherCode = 2,
            CapeJkg = 250
        }, Now);

    [Fact]
    public void BuildLogEntry_ContainsTheKeyOperationalFields()
    {
        string report = WeatherReportFormatter.BuildLogEntry(BuildAssessment(), Now, "Feuerwehrstraße 1, 60313 Frankfurt");

        Assert.Contains("WETTERMELDUNG", report);
        Assert.Contains("271200Z", report);           // tactical time
        Assert.Contains("N 50° 06.654'", report);     // chart-notation position
        Assert.Contains("Feuerwehrstraße 1", report); // address line
        Assert.Contains("Ausbreitungsrichtung", report);
        Assert.Contains("Ausbreitungsklasse", report);
        Assert.Contains("Sonnenaufgang", report);
        Assert.Contains("Deutscher Wetterdienst", report);
    }

    [Fact]
    public void BuildLogEntry_UsesGermanDecimalCommas()
    {
        string report = WeatherReportFormatter.BuildLogEntry(BuildAssessment(), Now);

        Assert.Contains("24,5 °C", report);
    }

    [Fact]
    public void BuildLogEntry_RendersDashesForMissingValues()
    {
        var sparse = WeatherAssessor.Assess(new WeatherSnapshot
        {
            Position = new GeoPosition(50.0, 8.0, PositionSource.Manual, Now),
            RetrievedAtUtc = Now
        }, Now);

        string report = WeatherReportFormatter.BuildLogEntry(sparse, Now);

        Assert.Contains("—", report);
        Assert.DoesNotContain("NaN", report);
    }

    [Fact]
    public void BuildRadioLine_IsASingleSpokenSentence()
    {
        string line = WeatherReportFormatter.BuildRadioLine(BuildAssessment());

        Assert.EndsWith(".", line);
        Assert.DoesNotContain("\n", line);
        Assert.Contains("Wind aus SW", line);
        Assert.Contains("Ausbreitung nach NO", line);
    }
}

public class CsvLoggerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildRow_ProducesSemicolonSeparatedGermanNumbers()
    {
        var assessment = WeatherAssessor.Assess(new WeatherSnapshot
        {
            Position = new GeoPosition(50.1109, 8.6821, PositionSource.Gps, Now, AccuracyM: 4),
            RetrievedAtUtc = Now,
            TemperatureC = 24.5,
            RelativeHumidityPercent = 58,
            WindSpeedMs = 5.2,
            WindDirectionDeg = 225
        }, Now);

        string row = SnapshotCsvLogger.BuildRow(assessment, Now);
        string[] fields = row.Split(';');

        Assert.Contains("24,5", fields);
        Assert.Contains("SW", fields);

        // Second column is the tactical time; its zone letter follows the machine's offset.
        Assert.Matches(@"^271[0-9]{3}[A-Z]JUL26$", fields[1]);
    }

    [Fact]
    public void BuildRow_LeavesMissingValuesEmptyRatherThanNaN()
    {
        var assessment = WeatherAssessor.Assess(new WeatherSnapshot
        {
            Position = new GeoPosition(50.0, 8.0, PositionSource.Manual, Now),
            RetrievedAtUtc = Now
        }, Now);

        string row = SnapshotCsvLogger.BuildRow(assessment, Now);

        Assert.DoesNotContain("NaN", row);
        Assert.Contains(";;", row);
    }

    [Fact]
    public async Task AppendAsync_WritesHeaderOnceAndOneRowPerCall()
    {
        string directory = Path.Combine(Path.GetTempPath(), "elw-meteo-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new SnapshotCsvLogger(directory);
            var assessment = WeatherAssessor.Assess(new WeatherSnapshot
            {
                Position = new GeoPosition(50.0, 8.0, PositionSource.Manual, Now),
                RetrievedAtUtc = Now,
                TemperatureC = 20.0,
                RelativeHumidityPercent = 50.0
            }, Now);

            await logger.AppendAsync(assessment, Now);
            await logger.AppendAsync(assessment, Now);

            string[] lines = await File.ReadAllLinesAsync(logger.FilePathFor(Now));

            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Zeitpunkt", lines[0].TrimStart('﻿'));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void FilePathFor_UsesOneFilePerLocalDay()
    {
        var logger = new SnapshotCsvLogger("/tmp/x");

        string first = logger.FilePathFor(Now);
        string second = logger.FilePathFor(Now.AddDays(1));

        Assert.NotEqual(first, second);
        Assert.EndsWith(".csv", first);
    }
}
