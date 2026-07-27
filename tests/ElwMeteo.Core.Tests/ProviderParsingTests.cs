using System.Text.Json;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class OpenMeteoUrlTests
{
    [Fact]
    public void BuildUrl_UsesInvariantDecimalSeparatorForCoordinates()
    {
        var position = new GeoPosition(50.1109, 8.6821, PositionSource.Manual, DateTimeOffset.UnixEpoch);
        string url = OpenMeteoWeatherProvider.BuildUrl(position);

        Assert.Contains("latitude=50.11090", url);
        Assert.Contains("longitude=8.68210", url);
        Assert.DoesNotContain(",", url.Split("latitude=")[1].Split('&')[0]);
    }

    [Fact]
    public void BuildUrl_RequestsEpochTimestampsAndMetresPerSecond()
    {
        var position = new GeoPosition(50.0, 8.0, PositionSource.Manual, DateTimeOffset.UnixEpoch);
        string url = OpenMeteoWeatherProvider.BuildUrl(position);

        Assert.Contains("timeformat=unixtime", url);
        Assert.Contains("wind_speed_unit=ms", url);
        Assert.Contains("timezone=UTC", url);
    }

    [Fact]
    public void BuildUrl_RequestsTheFieldsTheDashboardNeeds()
    {
        var position = new GeoPosition(50.0, 8.0, PositionSource.Manual, DateTimeOffset.UnixEpoch);
        string url = OpenMeteoWeatherProvider.BuildUrl(position);

        Assert.Contains("wind_gusts_10m", url);
        Assert.Contains("minutely_15=", url);
        Assert.Contains("lightning_potential", url);
        Assert.Contains("visibility", url);
        Assert.Contains("cape", url);
    }

    [Fact]
    public void BuildUrl_HandlesNegativeCoordinates()
    {
        var position = new GeoPosition(-33.8688, -151.2093, PositionSource.Manual, DateTimeOffset.UnixEpoch);
        string url = OpenMeteoWeatherProvider.BuildUrl(position);

        Assert.Contains("latitude=-33.86880", url);
        Assert.Contains("longitude=-151.20930", url);
    }
}

public class DwdWarningParsingTests
{
    private const string SampleFeatureCollection = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "properties": {
            "NAME": "Stadt Frankfurt am Main",
            "EVENT": "GEWITTER",
            "HEADLINE": "Amtliche WARNUNG vor GEWITTER",
            "LEVEL": 2,
            "SENT": "2026-07-27T09:00:00Z",
            "EXPIRES": "2026-07-27T14:00:00Z",
            "DESCRIPTION": "Es treten Gewitter auf.",
            "INSTRUCTION": "Meiden Sie freie Flaechen."
          }
        },
        {
          "type": "Feature",
          "properties": {
            "NAME": "Stadt Frankfurt am Main",
            "EVENT": "EXTREMES GEWITTER",
            "HEADLINE": "Amtliche UNWETTERWARNUNG",
            "LEVEL": 4,
            "SENT": "2026-07-27T10:00:00Z",
            "EXPIRES": "2026-07-27T13:00:00Z"
          }
        }
      ]
    }
    """;

    [Fact]
    public void ParseFeatureCollection_ReadsAllFields()
    {
        using var document = JsonDocument.Parse(SampleFeatureCollection);
        var warnings = DwdWarningProvider.ParseFeatureCollection(document.RootElement);

        Assert.Equal(2, warnings.Count);

        // Worst first.
        Assert.Equal(WarningLevel.Extreme, warnings[0].Level);
        Assert.Equal(WarningLevel.Moderate, warnings[1].Level);

        DwdWarning thunderstorm = warnings[1];
        Assert.Equal("GEWITTER", thunderstorm.Event);
        Assert.Equal("Amtliche WARNUNG vor GEWITTER", thunderstorm.Headline);
        Assert.Equal("Stadt Frankfurt am Main", thunderstorm.RegionName);
        Assert.Equal("Es treten Gewitter auf.", thunderstorm.Description);
        Assert.Equal("Meiden Sie freie Flaechen.", thunderstorm.Instruction);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero), thunderstorm.Start);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero), thunderstorm.End);
    }

    [Fact]
    public void ParseFeatureCollection_HandlesEmptyResult()
    {
        using var document = JsonDocument.Parse("""{"type":"FeatureCollection","features":[]}""");
        Assert.Empty(DwdWarningProvider.ParseFeatureCollection(document.RootElement));
    }

    [Fact]
    public void ParseFeatureCollection_HandleMissingFeaturesProperty()
    {
        using var document = JsonDocument.Parse("""{"type":"FeatureCollection"}""");
        Assert.Empty(DwdWarningProvider.ParseFeatureCollection(document.RootElement));
    }

    [Fact]
    public void ParseFeatureCollection_AcceptsCapSeverityWording()
    {
        using var document = JsonDocument.Parse("""
        {"features":[{"properties":{"HEADLINE":"Test","SEVERITY":"Severe","NAME":"X"}}]}
        """);

        var warnings = DwdWarningProvider.ParseFeatureCollection(document.RootElement);
        Assert.Equal(WarningLevel.Severe, warnings[0].Level);
    }

    [Fact]
    public void ParseFeatureCollection_AcceptsNumericLevelAsString()
    {
        using var document = JsonDocument.Parse("""
        {"features":[{"properties":{"HEADLINE":"Test","LEVEL":"3","NAME":"X"}}]}
        """);

        var warnings = DwdWarningProvider.ParseFeatureCollection(document.RootElement);
        Assert.Equal(WarningLevel.Severe, warnings[0].Level);
    }

    [Fact]
    public void ParseFeatureCollection_FallsBackWhenFieldsAreMissing()
    {
        using var document = JsonDocument.Parse("""{"features":[{"properties":{}}]}""");

        var warnings = DwdWarningProvider.ParseFeatureCollection(document.RootElement);
        Assert.Single(warnings);
        Assert.Equal("DWD-Warnung", warnings[0].Headline);
        Assert.Equal("—", warnings[0].RegionName);
    }

    [Fact]
    public void IsActiveAt_RespectsTheValidityWindow()
    {
        var warning = new DwdWarning("X", "X", WarningLevel.Minor,
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 27, 11, 0, 0, TimeSpan.Zero),
            "X", null, null);

        Assert.True(warning.IsActiveAt(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)));
        Assert.False(warning.IsActiveAt(new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero)));
        Assert.False(warning.IsActiveAt(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)));
    }
}

public class RainViewerParsingTests
{
    private const string SampleIndex = """
    {
      "version": "2.0",
      "generated": 1769511600,
      "host": "https://tilecache.rainviewer.com",
      "radar": {
        "past": [
          {"time": 1769510400, "path": "/v2/radar/1769510400"},
          {"time": 1769511000, "path": "/v2/radar/1769511000"}
        ],
        "nowcast": [
          {"time": 1769512200, "path": "/v2/radar/nowcast_1769512200"},
          {"time": 1769512800, "path": "/v2/radar/nowcast_1769512800"}
        ]
      }
    }
    """;

    [Fact]
    public void Parse_ReadsPastAndForecastFramesInOrder()
    {
        using var document = JsonDocument.Parse(SampleIndex);
        var timeline = RainViewerProvider.Parse(document.RootElement);

        Assert.Equal("https://tilecache.rainviewer.com", timeline.TileHost);
        Assert.Equal(4, timeline.Frames.Count);
        Assert.Equal(2, timeline.Past.Count());
        Assert.Equal(2, timeline.Forecast.Count());

        // Sorted ascending by time, forecast last.
        for (int i = 1; i < timeline.Frames.Count; i++)
        {
            Assert.True(timeline.Frames[i - 1].Time <= timeline.Frames[i].Time);
        }

        Assert.False(timeline.Frames[0].IsForecast);
        Assert.True(timeline.Frames[^1].IsForecast);
    }

    [Fact]
    public void TileUrlTemplate_LeavesLeafletPlaceholdersIntact()
    {
        using var document = JsonDocument.Parse(SampleIndex);
        var timeline = RainViewerProvider.Parse(document.RootElement);

        string url = timeline.TileUrlTemplate(timeline.Frames[0]);

        Assert.Equal(
            "https://tilecache.rainviewer.com/v2/radar/1769510400/512/{z}/{x}/{y}/4/1_1.png",
            url);
    }

    [Fact]
    public void TileUrlTemplate_HonoursColourAndFlagOptions()
    {
        using var document = JsonDocument.Parse(SampleIndex);
        var timeline = RainViewerProvider.Parse(document.RootElement);

        string url = timeline.TileUrlTemplate(timeline.Frames[0], colourScheme: 2, smooth: false, showSnow: false);

        Assert.EndsWith("/2/0_0.png", url);
    }

    [Fact]
    public void Parse_ReturnsEmptyTimelineWithoutRadarBlock()
    {
        using var document = JsonDocument.Parse("""{"host":"https://example.invalid"}""");
        Assert.Empty(RainViewerProvider.Parse(document.RootElement).Frames);
    }

    [Fact]
    public void Parse_ReadsInfraredSatelliteFrames()
    {
        using var document = JsonDocument.Parse("""
        {
          "host": "https://tilecache.rainviewer.com",
          "radar": {"past": [{"time": 1769510400, "path": "/v2/radar/1769510400"}]},
          "satellite": {"infrared": [
            {"time": 1769510100, "path": "/v2/satellite/a"},
            {"time": 1769510700, "path": "/v2/satellite/b"}
          ]}
        }
        """);

        var timeline = RainViewerProvider.Parse(document.RootElement);

        Assert.Single(timeline.Frames);
        Assert.Equal(2, timeline.SatelliteFrames.Count);
    }

    [Fact]
    public void SatelliteFrameNear_PicksTheClosestFrameInTime()
    {
        var timeline = new RadarTimeline(
            "https://h",
            [],
            [
                new RadarFrame(DateTimeOffset.UnixEpoch.AddMinutes(0), "/a", false),
                new RadarFrame(DateTimeOffset.UnixEpoch.AddMinutes(30), "/b", false)
            ]);

        Assert.Equal("/a", timeline.SatelliteFrameNear(DateTimeOffset.UnixEpoch.AddMinutes(10))!.Path);
        Assert.Equal("/b", timeline.SatelliteFrameNear(DateTimeOffset.UnixEpoch.AddMinutes(20))!.Path);
    }

    [Fact]
    public void SatelliteFrameNear_ReturnsNullWithoutSatelliteData()
    {
        Assert.Null(RadarTimeline.Empty.SatelliteFrameNear(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void SatelliteTileUrlTemplate_UsesTheSatelliteColourScheme()
    {
        var timeline = new RadarTimeline("https://h", [],
            [new RadarFrame(DateTimeOffset.UnixEpoch, "/v2/satellite/a", false)]);

        Assert.Equal(
            "https://h/v2/satellite/a/512/{z}/{x}/{y}/0/0_0.png",
            timeline.SatelliteTileUrlTemplate(timeline.SatelliteFrames[0]));
    }

    [Fact]
    public void ColourSchemes_CoverTheRainViewerRange()
    {
        Assert.Equal(9, RadarTimeline.ColourSchemes.Count);
        Assert.Equal(Enumerable.Range(0, 9), RadarTimeline.ColourSchemes.Select(s => s.Id));
    }

    [Fact]
    public void Parse_SkipsMalformedFrames()
    {
        using var document = JsonDocument.Parse("""
        {"host":"h","radar":{"past":[{"time":1},{"path":"/x"},{"time":2,"path":""},{"time":3,"path":"/ok"}]}}
        """);

        var timeline = RainViewerProvider.Parse(document.RootElement);
        Assert.Single(timeline.Frames);
        Assert.Equal("/ok", timeline.Frames[0].Path);
    }

    [Fact]
    public void RelativeLabel_DescribesFramesRelativeToNow()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(12);
        var frame = new RadarFrame(now.AddMinutes(20), "/x", true);

        Assert.Equal("+20 min", frame.RelativeLabel(now));
        Assert.Equal("jetzt", new RadarFrame(now, "/x", false).RelativeLabel(now));
        Assert.Equal("-10 min", new RadarFrame(now.AddMinutes(-10), "/x", false).RelativeLabel(now));
    }
}

public class GeocodingParsingTests
{
    [Fact]
    public void BuildAddressLine_AssemblesStreetAndLocality()
    {
        using var document = JsonDocument.Parse("""
        {
          "display_name": "long ignored value",
          "address": {
            "road": "Feuerwehrstraße",
            "house_number": "1",
            "postcode": "60313",
            "city": "Frankfurt am Main"
          }
        }
        """);

        Assert.Equal("Feuerwehrstraße 1, 60313 Frankfurt am Main",
            GeocodingService.BuildAddressLine(document.RootElement));
    }

    [Fact]
    public void BuildAddressLine_AppendsDistinctDistrict()
    {
        using var document = JsonDocument.Parse("""
        {"address":{"road":"Hauptstraße","city":"Frankfurt am Main","city_district":"Bornheim"}}
        """);

        Assert.Equal("Hauptstraße, Frankfurt am Main, (Bornheim)",
            GeocodingService.BuildAddressLine(document.RootElement));
    }

    [Fact]
    public void BuildAddressLine_FallsBackToDisplayNameWithoutAddressBlock()
    {
        using var document = JsonDocument.Parse("""{"display_name":"Somewhere"}""");
        Assert.Equal("Somewhere", GeocodingService.BuildAddressLine(document.RootElement));
    }

    [Fact]
    public void BuildAddressLine_FallsBackToDisplayNameForUnusableAddress()
    {
        using var document = JsonDocument.Parse("""{"display_name":"Feldweg","address":{"country":"Deutschland"}}""");
        Assert.Equal("Feldweg", GeocodingService.BuildAddressLine(document.RootElement));
    }
}
