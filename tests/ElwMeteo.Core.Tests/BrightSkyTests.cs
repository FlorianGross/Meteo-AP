using System.Text.Json;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class BrightSkyStationTests
{
    private const string CurrentWeather = """
    {
      "weather": {
        "source_id": 238685,
        "timestamp": "2026-07-27T12:30:00+00:00",
        "cloud_cover": 87,
        "condition": "dry",
        "dew_point": 12.87,
        "precipitation_10": 0.2,
        "pressure_msl": 1015.5,
        "relative_humidity": 66,
        "visibility": 34400,
        "wind_direction_10": 240,
        "wind_speed_10": 18.0,
        "wind_gust_speed_10": 36.0,
        "temperature": 19.4,
        "icon": "cloudy"
      },
      "sources": [
        {
          "id": 238685,
          "dwd_station_id": "01766",
          "station_name": "Muenster/Osnabrueck",
          "lat": 51.9319,
          "lon": 7.5556,
          "distance": 16008
        }
      ]
    }
    """;

    [Fact]
    public void ParseCurrentWeather_ReadsStationAndMeasurements()
    {
        using var document = JsonDocument.Parse(CurrentWeather);
        StationObservation? observation = BrightSkyProvider.ParseCurrentWeather(document.RootElement);

        Assert.NotNull(observation);
        Assert.Equal("Muenster/Osnabrueck", observation!.StationName);
        Assert.Equal(16008, observation.DistanceM);
        Assert.Equal(19.4, observation.TemperatureC);
        Assert.Equal(12.87, observation.DewPointC);
        Assert.Equal(66, observation.RelativeHumidityPercent);
        Assert.Equal(1015.5, observation.PressureMslHpa);
        Assert.Equal(240, observation.WindDirectionDeg);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 12, 30, 0, TimeSpan.Zero), observation.Timestamp);
    }

    [Fact]
    public void ParseCurrentWeather_ConvertsWindFromKilometresPerHour()
    {
        using var document = JsonDocument.Parse(CurrentWeather);
        StationObservation observation = BrightSkyProvider.ParseCurrentWeather(document.RootElement)!;

        // Bright Sky reports km/h; the application works in m/s throughout.
        Assert.Equal(5.0, observation.WindSpeedMs!.Value, 1e-6);
        Assert.Equal(10.0, observation.WindGustMs!.Value, 1e-6);
    }

    [Fact]
    public void ParseCurrentWeather_FallsBackToHourlyWindFields()
    {
        using var document = JsonDocument.Parse("""
        {
          "weather": {"timestamp":"2026-07-27T12:00:00+00:00","wind_speed":36.0,"wind_direction":90},
          "sources": [{"station_name":"Test","distance":1200}]
        }
        """);

        StationObservation observation = BrightSkyProvider.ParseCurrentWeather(document.RootElement)!;

        Assert.Equal(10.0, observation.WindSpeedMs!.Value, 1e-6);
        Assert.Equal(90, observation.WindDirectionDeg);
    }

    [Fact]
    public void ParseCurrentWeather_ReturnsNullWithoutAWeatherBlock()
    {
        using var document = JsonDocument.Parse("""{"sources":[]}""");
        Assert.Null(BrightSkyProvider.ParseCurrentWeather(document.RootElement));
    }

    [Fact]
    public void ParseCurrentWeather_ReturnsNullWithoutATimestamp()
    {
        using var document = JsonDocument.Parse("""{"weather":{"temperature":19.4}}""");
        Assert.Null(BrightSkyProvider.ParseCurrentWeather(document.RootElement));
    }

    [Fact]
    public void ParseCurrentWeather_ToleratesAMissingSourceList()
    {
        using var document = JsonDocument.Parse("""
        {"weather":{"timestamp":"2026-07-27T12:00:00+00:00","temperature":10.0}}
        """);

        StationObservation observation = BrightSkyProvider.ParseCurrentWeather(document.RootElement)!;

        Assert.Equal("DWD-Station", observation.StationName);
        Assert.Equal(0, observation.DistanceM);
    }

    [Theory]
    [InlineData(400, "400 m")]
    [InlineData(16008, "16,0 km")]
    public void DistanceLabel_SwitchesUnitAtAKilometre(double metres, string expected)
    {
        var observation = new StationObservation(
            "X", metres, DateTimeOffset.UnixEpoch,
            null, null, null, null, null, null, null, null, null, null);

        // The label follows the current culture's decimal separator.
        Assert.Equal(expected.Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
            observation.DistanceLabel);
    }

    [Fact]
    public void IsRepresentative_MarksDistantStations()
    {
        StationObservation Build(double metres) => new(
            "X", metres, DateTimeOffset.UnixEpoch,
            null, null, null, null, null, null, null, null, null, null);

        Assert.True(Build(8_000).IsRepresentative);
        Assert.False(Build(60_000).IsRepresentative);
    }
}

public class BrightSkyAlertTests
{
    private const string Alerts = """
    {
      "alerts": [
        {
          "id": 279977,
          "onset": "2026-07-27T10:00:00+00:00",
          "expires": "2026-07-27T15:00:00+00:00",
          "category": "met",
          "severity": "moderate",
          "event_en": "wind gusts",
          "event_de": "WINDBÖEN",
          "headline_de": "Amtliche WARNUNG vor WINDBÖEN",
          "instruction_de": "Achten Sie auf herabfallende Gegenstaende.",
          "description_de": "Es treten Windboeen mit Geschwindigkeiten um 60 km/h auf."
        },
        {
          "id": 279978,
          "onset": "2026-07-27T12:00:00+00:00",
          "expires": "2026-07-27T18:00:00+00:00",
          "severity": "extreme",
          "event_de": "EXTREMES GEWITTER",
          "headline_de": "Amtliche UNWETTERWARNUNG vor EXTREMEM GEWITTER"
        }
      ],
      "location": {
        "warn_cell_id": 803159016,
        "name": "Stadt Köln",
        "name_short": "Köln",
        "district": "Köln",
        "state": "Nordrhein-Westfalen"
      }
    }
    """;

    [Fact]
    public void ParseAlerts_ReadsGermanTextsAndOrdersWorstFirst()
    {
        using var document = JsonDocument.Parse(Alerts);
        var warnings = BrightSkyProvider.ParseAlerts(document.RootElement);

        Assert.Equal(2, warnings.Count);
        Assert.Equal(WarningLevel.Extreme, warnings[0].Level);
        Assert.Equal(WarningLevel.Moderate, warnings[1].Level);

        DwdWarning gusts = warnings[1];
        Assert.Equal("WINDBÖEN", gusts.Event);
        Assert.Equal("Amtliche WARNUNG vor WINDBÖEN", gusts.Headline);
        Assert.Equal("Stadt Köln", gusts.RegionName);
        Assert.Contains("Windboeen", gusts.Description);
        Assert.Contains("herabfallende", gusts.Instruction);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero), gusts.Start);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 15, 0, 0, TimeSpan.Zero), gusts.End);
    }

    [Theory]
    [InlineData("minor", WarningLevel.Minor)]
    [InlineData("moderate", WarningLevel.Moderate)]
    [InlineData("severe", WarningLevel.Severe)]
    [InlineData("extreme", WarningLevel.Extreme)]
    [InlineData("Extreme", WarningLevel.Extreme)]
    [InlineData("unbekannt", WarningLevel.Minor)]
    public void ParseAlerts_MapsCapSeverity(string severity, WarningLevel expected)
    {
        using var document = JsonDocument.Parse(
            $$$"""{"alerts":[{"severity":"{{{severity}}}","headline_de":"X"}],"location":{"name":"Y"}}""");

        Assert.Equal(expected, BrightSkyProvider.ParseAlerts(document.RootElement)[0].Level);
    }

    [Fact]
    public void ParseAlerts_FallsBackToEnglishWhenGermanIsAbsent()
    {
        using var document = JsonDocument.Parse("""
        {"alerts":[{"severity":"severe","headline_en":"Official WARNING","event_en":"storm"}]}
        """);

        var warning = BrightSkyProvider.ParseAlerts(document.RootElement)[0];

        Assert.Equal("Official WARNING", warning.Headline);
        Assert.Equal("storm", warning.Event);
        Assert.Equal("—", warning.RegionName);
    }

    [Fact]
    public void ParseAlerts_HandlesAnEmptyOrAbsentList()
    {
        using var empty = JsonDocument.Parse("""{"alerts":[],"location":{"name":"X"}}""");
        Assert.Empty(BrightSkyProvider.ParseAlerts(empty.RootElement));

        using var missing = JsonDocument.Parse("""{"location":{"name":"X"}}""");
        Assert.Empty(BrightSkyProvider.ParseAlerts(missing.RootElement));
    }
}

public class BrightSkyRadarTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Bright Sky returns a small grid per frame plus the fractional pixel the
    /// requested coordinates land on. Cell values are hundredths of a millimetre
    /// per five minutes.
    /// </summary>
    private const string Radar = """
    {
      "radar": [
        {
          "timestamp": "2026-07-27T11:55:00+00:00",
          "source": "RADOLAN::WN::2026-07-27T11:55:00+00:00",
          "precipitation_5": [[0, 0, 0], [0, 25, 0], [0, 0, 0]]
        },
        {
          "timestamp": "2026-07-27T12:00:00+00:00",
          "source": "RADOLAN::WN::2026-07-27T12:00:00+00:00",
          "precipitation_5": [[0, 0, 0], [0, 50, 0], [0, 0, 0]]
        },
        {
          "timestamp": "2026-07-27T12:05:00+00:00",
          "source": "RADOLAN::RV::2026-07-27T12:00:00+00:00",
          "precipitation_5": [[0, 0, 0], [0, 100, 0], [0, 0, 0]]
        }
      ],
      "latlon_position": {"x": 1.2, "y": 1.4},
      "bbox": [0, 0, 3, 3]
    }
    """;

    [Fact]
    public void ParseRadar_ReadsTheCellUnderTheRequestedPosition()
    {
        using var document = JsonDocument.Parse(Radar);
        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        Assert.Equal(3, series.Steps.Count);

        // 25 hundredths of a mm per 5 min == 0.25 mm / 5 min == 3 mm/h.
        Assert.Equal(3.0, series.Steps[0].MillimetresPerHour, 1e-6);
        Assert.Equal(6.0, series.Steps[1].MillimetresPerHour, 1e-6);
        Assert.Equal(12.0, series.Steps[2].MillimetresPerHour, 1e-6);
    }

    [Fact]
    public void ParseRadar_SeparatesMeasurementFromExtrapolation()
    {
        using var document = JsonDocument.Parse(Radar);
        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        Assert.False(series.Steps[0].IsForecast);
        Assert.False(series.Steps[1].IsForecast);
        Assert.True(series.Steps[2].IsForecast);
    }

    [Fact]
    public void ParseRadar_OrdersStepsByTime()
    {
        using var document = JsonDocument.Parse("""
        {
          "radar": [
            {"timestamp":"2026-07-27T12:10:00+00:00","precipitation_5":[[10]]},
            {"timestamp":"2026-07-27T11:50:00+00:00","precipitation_5":[[20]]}
          ],
          "latlon_position": {"x": 0, "y": 0}
        }
        """);

        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        Assert.True(series.Steps[0].Time < series.Steps[1].Time);
    }

    [Fact]
    public void ParseRadar_ClampsAPositionOutsideTheGrid()
    {
        using var document = JsonDocument.Parse("""
        {
          "radar": [{"timestamp":"2026-07-27T12:00:00+00:00","precipitation_5":[[7, 8]]}],
          "latlon_position": {"x": 99, "y": 99}
        }
        """);

        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        // Clamped to the last cell rather than dropping the frame.
        Assert.Single(series.Steps);
        Assert.Equal(8 * 0.01 * 12, series.Steps[0].MillimetresPerHour, 1e-6);
    }

    [Fact]
    public void ParseRadar_SkipsFramesWithoutUsableData()
    {
        using var document = JsonDocument.Parse("""
        {
          "radar": [
            {"source":"no timestamp","precipitation_5":[[10]]},
            {"timestamp":"2026-07-27T12:00:00+00:00"},
            {"timestamp":"2026-07-27T12:05:00+00:00","precipitation_5":[]},
            {"timestamp":"2026-07-27T12:10:00+00:00","precipitation_5":[[30]]}
          ],
          "latlon_position": {"x": 0, "y": 0}
        }
        """);

        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        Assert.Single(series.Steps);
        Assert.Equal(3.6, series.Steps[0].MillimetresPerHour, 1e-6);
    }

    [Fact]
    public void ParseRadar_ReturnsEmptyWithoutARadarBlock()
    {
        using var document = JsonDocument.Parse("""{"bbox":[0,0,1,1]}""");
        Assert.True(BrightSkyProvider.ParseRadar(document.RootElement, Now).IsEmpty);
    }

    [Fact]
    public void Series_ReportsPeakAndTheValueAtAnInstant()
    {
        using var document = JsonDocument.Parse(Radar);
        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);

        Assert.Equal(12.0, series.PeakMillimetresPerHour, 1e-6);
        Assert.Equal(6.0, series.At(Now)!.MillimetresPerHour, 1e-6);
    }

    [Fact]
    public void Series_FindsTheFirstWetForecastStep()
    {
        using var document = JsonDocument.Parse("""
        {
          "radar": [
            {"timestamp":"2026-07-27T12:05:00+00:00","precipitation_5":[[0]]},
            {"timestamp":"2026-07-27T12:10:00+00:00","precipitation_5":[[0]]},
            {"timestamp":"2026-07-27T12:15:00+00:00","precipitation_5":[[40]]}
          ],
          "latlon_position": {"x": 0, "y": 0}
        }
        """);

        RadarPointSeries series = BrightSkyProvider.ParseRadar(document.RootElement, Now);
        RadarPointStep? first = series.FirstWetForecast;

        Assert.NotNull(first);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 12, 15, 0, TimeSpan.Zero), first!.Time);
    }

    [Fact]
    public void Series_ReportsNoWetForecastForADrySeries()
    {
        using var document = JsonDocument.Parse("""
        {
          "radar": [{"timestamp":"2026-07-27T12:30:00+00:00","precipitation_5":[[0]]}],
          "latlon_position": {"x": 0, "y": 0}
        }
        """);

        Assert.Null(BrightSkyProvider.ParseRadar(document.RootElement, Now).FirstWetForecast);
        Assert.Null(RadarPointSeries.Empty.At(Now));
    }
}
