using System.Xml.Linq;
using ElwMeteo.Core.Maps;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WmsTimeDimensionParsingTests
{
    [Fact]
    public void Parse_ReadsAnExplicitListOfInstants()
    {
        var dimension = WmsTimeDimension.Parse(
            "2026-07-27T10:00:00Z,2026-07-27T10:05:00Z,2026-07-27T10:10:00Z");

        Assert.Equal(3, dimension.Instants.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero), dimension.Instants[0]);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 10, 10, 0, TimeSpan.Zero), dimension.Instants[2]);
    }

    [Fact]
    public void Parse_ExpandsAStartEndPeriodInterval()
    {
        // The form GeoServer uses for the DWD radar: five-minute steps.
        var dimension = WmsTimeDimension.Parse(
            "2026-07-27T10:00:00Z/2026-07-27T10:30:00Z/PT5M");

        Assert.Equal(7, dimension.Instants.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero), dimension.Instants[0]);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 10, 30, 0, TimeSpan.Zero), dimension.Instants[^1]);
    }

    [Fact]
    public void Parse_HandlesSeveralIntervalsInOneValue()
    {
        var dimension = WmsTimeDimension.Parse(
            "2026-07-27T09:00:00Z/2026-07-27T09:10:00Z/PT5M,2026-07-27T12:00:00Z/2026-07-27T12:10:00Z/PT5M");

        Assert.Equal(6, dimension.Instants.Count);
    }

    [Fact]
    public void Parse_MixesListAndIntervalEntries()
    {
        var dimension = WmsTimeDimension.Parse(
            "2026-07-27T08:00:00Z,2026-07-27T10:00:00Z/2026-07-27T10:10:00Z/PT5M");

        Assert.Equal(4, dimension.Instants.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero), dimension.Instants[0]);
    }

    [Fact]
    public void Parse_ReturnsBothEndpointsForAnIntervalWithoutAPeriod()
    {
        var dimension = WmsTimeDimension.Parse("2026-07-27T10:00:00Z/2026-07-27T12:00:00Z");

        Assert.Equal(2, dimension.Instants.Count);
    }

    [Fact]
    public void Parse_SortsAndDeduplicates()
    {
        var dimension = WmsTimeDimension.Parse(
            "2026-07-27T10:10:00Z,2026-07-27T10:00:00Z,2026-07-27T10:10:00Z");

        Assert.Equal(2, dimension.Instants.Count);
        Assert.True(dimension.Instants[0] < dimension.Instants[1]);
    }

    [Fact]
    public void Parse_CapsAnAbsurdlyLongSeries()
    {
        // A server advertising a year of five-minute steps must not be expanded.
        var dimension = WmsTimeDimension.Parse(
            "2026-01-01T00:00:00Z/2026-12-31T00:00:00Z/PT5M", maxInstants: 100);

        Assert.Equal(100, dimension.Instants.Count);
    }

    [Fact]
    public void Parse_ReturnsEmptyForUnusableInput()
    {
        Assert.True(WmsTimeDimension.Parse(null).IsEmpty);
        Assert.True(WmsTimeDimension.Parse("").IsEmpty);
        Assert.True(WmsTimeDimension.Parse("   ").IsEmpty);
        Assert.True(WmsTimeDimension.Parse("kein Zeitstempel").IsEmpty);
        Assert.True(WmsTimeDimension.Empty.IsEmpty);
    }

    [Fact]
    public void Parse_SkipsAnIntervalWithAnUnparseableEndpoint()
    {
        var dimension = WmsTimeDimension.Parse("kaputt/2026-07-27T10:00:00Z/PT5M,2026-07-27T11:00:00Z");

        Assert.Single(dimension.Instants);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 11, 0, 0, TimeSpan.Zero), dimension.Instants[0]);
    }

    [Theory]
    [InlineData("PT5M", 5 * 60)]
    [InlineData("PT1H", 3600)]
    [InlineData("PT30S", 30)]
    [InlineData("P1D", 86400)]
    [InlineData("PT1H30M", 5400)]
    [InlineData("P1DT2H", 93600)]
    [InlineData("pt5m", 5 * 60)]
    public void ParsePeriod_ReadsIso8601Durations(string period, double expectedSeconds)
    {
        TimeSpan? parsed = WmsTimeDimension.ParsePeriod(period);

        Assert.NotNull(parsed);
        Assert.Equal(expectedSeconds, parsed!.Value.TotalSeconds, 1e-6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5M")]
    [InlineData("PT")]
    [InlineData("P0D")]
    // Months and years are not fixed-length and no radar product uses them.
    [InlineData("P1M")]
    [InlineData("P1Y")]
    public void ParsePeriod_RejectsWhatItCannotResolve(string period)
    {
        Assert.Null(WmsTimeDimension.ParsePeriod(period));
    }

    [Fact]
    public void Format_ProducesUtcIso8601()
    {
        var instant = new DateTimeOffset(2026, 7, 27, 12, 5, 0, TimeSpan.FromHours(2));

        // Same instant, expressed as Zulu — what a WMS TIME parameter expects.
        Assert.Equal("2026-07-27T10:05:00Z", WmsTimeDimension.Format(instant));
    }

    [Fact]
    public void ObservedAndForecast_SplitAtTheGivenInstant()
    {
        var now = new DateTimeOffset(2026, 7, 27, 10, 10, 0, TimeSpan.Zero);
        var dimension = WmsTimeDimension.Parse("2026-07-27T10:00:00Z/2026-07-27T10:20:00Z/PT5M");

        Assert.Equal(3, dimension.Observed(now).Count());  // 10:00, 10:05, 10:10
        Assert.Equal(2, dimension.Forecast(now).Count());  // 10:15, 10:20
    }
}

public class WmsCapabilitiesTimeTests
{
    /// <summary>WMS 1.3.0 carries the values inside the Dimension element.</summary>
    private const string Wms130 = """
    <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
      <Capability><Layer>
        <Layer>
          <Name>dwd:WN-Produkt</Name>
          <Title>Radarkomposit mit Vorhersage</Title>
          <Dimension name="time" units="ISO8601" default="2026-07-27T10:00:00Z">2026-07-27T09:00:00Z/2026-07-27T11:00:00Z/PT5M</Dimension>
        </Layer>
        <Layer>
          <Name>dwd:Warnungen_Gemeinden</Name>
          <Title>Warnungen</Title>
        </Layer>
      </Layer></Capability>
    </WMS_Capabilities>
    """;

    /// <summary>WMS 1.1.1 splits the declaration from the values.</summary>
    private const string Wms111 = """
    <WMT_MS_Capabilities version="1.1.1">
      <Capability><Layer>
        <Layer>
          <Name>dwd:WN-Produkt</Name>
          <Title>Radar</Title>
          <Dimension name="time" units="ISO8601"/>
          <Extent name="time" default="2026-07-27T10:00:00Z">2026-07-27T10:00:00Z,2026-07-27T10:05:00Z</Extent>
        </Layer>
      </Layer></Capability>
    </WMT_MS_Capabilities>
    """;

    [Fact]
    public void ParseLayers_ReadsTheTimeDimensionFromWms130()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Wms130));
        WmsLayerInfo radar = layers.Single(l => l.Name == "dwd:WN-Produkt");

        Assert.True(radar.IsAnimatable);
        Assert.Equal(25, radar.Time.Instants.Count); // two hours in five-minute steps
        Assert.Equal("2026-07-27T10:00:00Z", radar.Time.DefaultValue);
    }

    [Fact]
    public void ParseLayers_ReadsTheExtentElementFromWms111()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Wms111));
        WmsLayerInfo radar = layers.Single(l => l.Name == "dwd:WN-Produkt");

        // The empty Dimension declaration must not shadow the Extent values.
        Assert.True(radar.IsAnimatable);
        Assert.Equal(2, radar.Time.Instants.Count);
    }

    [Fact]
    public void ParseLayers_LeavesLayersWithoutATimeDimensionStatic()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Wms130));
        WmsLayerInfo warnings = layers.Single(l => l.Name == "dwd:Warnungen_Gemeinden");

        Assert.False(warnings.IsAnimatable);
        Assert.True(warnings.Time.IsEmpty);
    }

    [Fact]
    public void ParseLayers_IgnoresANonTimeDimension()
    {
        var document = XDocument.Parse("""
        <WMS_Capabilities xmlns="http://www.opengis.net/wms"><Capability><Layer>
          <Layer>
            <Name>dwd:X</Name><Title>X</Title>
            <Dimension name="elevation" units="m">0,100,200</Dimension>
          </Layer>
        </Layer></Capability></WMS_Capabilities>
        """);

        Assert.False(WmsCapabilitiesService.ParseLayers(document)[0].IsAnimatable);
    }
}

public class AnimatedRadarSourceTests
{
    [Fact]
    public void TheOfficialGermanRadarIsAnimatable()
    {
        // The point of the WN product: an official source with a timeline.
        RadarSourceDefinition wn = RadarSourceCatalog.ById("dwd-wn");

        Assert.True(wn.UsesTimeDimension);
        Assert.True(wn.SupportsTimeline);
        Assert.Equal(RadarSourceKind.Wms, wn.Kind);
    }

    [Fact]
    public void TimeEnabledSourcesListCandidateLayerNames()
    {
        // The DWD renames products, so a source must offer alternatives that the
        // capability check can resolve against the live server.
        var wmsSources = RadarSourceCatalog.All.Where(s => s.Kind == RadarSourceKind.Wms);

        Assert.All(wmsSources, s => Assert.NotEmpty(s.WmsLayerCandidates));
        Assert.All(wmsSources, s => Assert.Contains(s.WmsLayers, s.WmsLayerCandidates));
    }

    [Fact]
    public void AStillImageSourceDoesNotClaimATimeline()
    {
        RadarSourceDefinition still = RadarSourceCatalog.ById("dwd-radolan");

        Assert.False(still.UsesTimeDimension);
        Assert.False(still.SupportsTimeline);
    }

    [Fact]
    public void MoreThanOneSourceNowOffersATimeline()
    {
        // Before the WN product only RainViewer could animate.
        Assert.True(RadarSourceCatalog.All.Count(s => s.SupportsTimeline) >= 2);
    }
}
