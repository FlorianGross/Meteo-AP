using System.Xml.Linq;
using ElwMeteo.Core.Maps;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WmsCapabilitiesTests
{
    /// <summary>
    /// Shaped like a GeoServer WMS 1.3.0 answer: a namespaced document with an
    /// unnamed container layer wrapping the requestable ones.
    /// </summary>
    private const string Capabilities = """
    <?xml version="1.0" encoding="UTF-8"?>
    <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
      <Service><Name>WMS</Name><Title>DWD GeoServer</Title></Service>
      <Capability>
        <Layer>
          <Title>Deutscher Wetterdienst</Title>
          <Layer queryable="1">
            <Name>dwd:Niederschlagsradar</Name>
            <Title>Niederschlagsradar</Title>
            <Abstract>RADOLAN-Komposit</Abstract>
          </Layer>
          <Layer queryable="1">
            <Name>dwd:Warnungen_Gemeinden</Name>
            <Title>Warnungen Gemeinden</Title>
          </Layer>
          <Layer queryable="0">
            <Name>dwd:FX-Produkt</Name>
            <Title>FX-Produkt</Title>
          </Layer>
        </Layer>
      </Capability>
    </WMS_Capabilities>
    """;

    [Fact]
    public void ParseLayers_ReadsNamedLayersThroughTheNamespace()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Capabilities));

        Assert.Equal(3, layers.Count);
        Assert.Contains(layers, l => l.Name == "dwd:Niederschlagsradar");
        Assert.Contains(layers, l => l.Name == "dwd:Warnungen_Gemeinden");
        Assert.Contains(layers, l => l.Name == "dwd:FX-Produkt");
    }

    [Fact]
    public void ParseLayers_SkipsTheUnnamedContainerLayer()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Capabilities));

        // The wrapper carries a title but no name and cannot be requested.
        Assert.DoesNotContain(layers, l => l.Title == "Deutscher Wetterdienst");
    }

    [Fact]
    public void ParseLayers_KeepsTitleAndAbstract()
    {
        var layers = WmsCapabilitiesService.ParseLayers(XDocument.Parse(Capabilities));
        WmsLayerInfo radar = layers.Single(l => l.Name == "dwd:Niederschlagsradar");

        Assert.Equal("Niederschlagsradar", radar.Title);
        Assert.Equal("RADOLAN-Komposit", radar.Abstract);
    }

    [Fact]
    public void ParseLayers_WorksWithoutANamespace()
    {
        // WMS 1.1.1 responses come back without the default namespace.
        var document = XDocument.Parse("""
        <WMT_MS_Capabilities version="1.1.1">
          <Capability><Layer><Title>root</Title>
            <Layer><Name>dwd:Test</Name><Title>Test</Title></Layer>
          </Layer></Capability>
        </WMT_MS_Capabilities>
        """);

        var layers = WmsCapabilitiesService.ParseLayers(document);

        Assert.Single(layers);
        Assert.Equal("dwd:Test", layers[0].Name);
    }

    [Fact]
    public void ParseLayers_DeduplicatesRepeatedNames()
    {
        var document = XDocument.Parse("""
        <WMS_Capabilities xmlns="http://www.opengis.net/wms"><Capability><Layer>
          <Layer><Name>dwd:A</Name><Title>A</Title></Layer>
          <Layer><Name>dwd:A</Name><Title>A nochmal</Title></Layer>
        </Layer></Capability></WMS_Capabilities>
        """);

        Assert.Single(WmsCapabilitiesService.ParseLayers(document));
    }

    [Fact]
    public void ParseLayers_ReturnsEmptyForADocumentWithoutLayers()
    {
        var document = XDocument.Parse("""
        <WMS_Capabilities xmlns="http://www.opengis.net/wms"><Service><Name>WMS</Name></Service></WMS_Capabilities>
        """);

        Assert.Empty(WmsCapabilitiesService.ParseLayers(document));
    }

    [Fact]
    public void ShortName_DropsTheWorkspacePrefix()
    {
        Assert.Equal("Niederschlagsradar", new WmsLayerInfo("dwd:Niederschlagsradar", "t", null).ShortName);
        Assert.Equal("ohnePrefix", new WmsLayerInfo("ohnePrefix", "t", null).ShortName);
        // A trailing colon has no name after it; keep the original rather than an empty string.
        Assert.Equal("dwd:", new WmsLayerInfo("dwd:", "t", null).ShortName);
    }
}

public class RadarSourceCatalogTests
{
    [Fact]
    public void All_OffersAnAnimatedAndAnOfficialSource()
    {
        Assert.Contains(RadarSourceCatalog.All, s => s.Kind == RadarSourceKind.RainViewerFrames);
        Assert.Contains(RadarSourceCatalog.All, s => s.Kind == RadarSourceKind.Wms);
    }

    [Fact]
    public void ATimelineIsClaimedOnlyWhereOneCanBeBuilt()
    {
        // The slider and play button need either explicit frames or a TIME
        // dimension; nothing else may claim to be animatable.
        Assert.All(
            RadarSourceCatalog.All.Where(s => s.SupportsTimeline),
            s => Assert.True(s.Kind == RadarSourceKind.RainViewerFrames || s.UsesTimeDimension));

        Assert.True(RadarSourceCatalog.Default.SupportsTimeline);
    }

    [Fact]
    public void KeyedSourcesAreMarkedAsRequiringAKey()
    {
        var keyed = RadarSourceCatalog.All.Where(s => s.Kind == RadarSourceKind.KeyedTiles).ToList();

        Assert.NotEmpty(keyed);
        Assert.All(keyed, s => Assert.True(s.RequiresApiKey));
        Assert.All(keyed, s => Assert.Contains("{key}", s.TileUrlTemplate));
    }

    [Fact]
    public void FreeSourcesDoNotRequireAKey()
    {
        var free = RadarSourceCatalog.All.Where(s => s.Kind != RadarSourceKind.KeyedTiles);
        Assert.All(free, s => Assert.False(s.RequiresApiKey));
    }

    [Fact]
    public void WmsSourcesCarryAnEndpointAndLayerName()
    {
        var wms = RadarSourceCatalog.All.Where(s => s.Kind == RadarSourceKind.Wms).ToList();

        Assert.NotEmpty(wms);
        Assert.All(wms, s => Assert.False(string.IsNullOrWhiteSpace(s.WmsUrl)));
        Assert.All(wms, s => Assert.False(string.IsNullOrWhiteSpace(s.WmsLayers)));
    }

    [Fact]
    public void ById_FallsBackToTheDefaultForAnUnknownId()
    {
        Assert.Equal(RadarSourceCatalog.Default, RadarSourceCatalog.ById("gibt-es-nicht"));
        Assert.Equal(RadarSourceCatalog.Default, RadarSourceCatalog.ById(null));
        Assert.Equal("dwd-radolan", RadarSourceCatalog.ById("dwd-radolan").Id);
    }

    [Fact]
    public void EverySourceHasAZoomCeiling()
    {
        Assert.All(RadarSourceCatalog.All, s => Assert.InRange(s.MaxUsefulZoom, 6, 16));
    }
}

public class MapLayerCatalogTests
{
    [Fact]
    public void EveryLayerCarriesAnAttribution()
    {
        // Every source used here requires attribution under its licence.
        Assert.All(MapLayerCatalog.All, l => Assert.False(string.IsNullOrWhiteSpace(l.Attribution)));
    }

    [Fact]
    public void LayerIdsAreUnique()
    {
        var duplicates = MapLayerCatalog.All
            .GroupBy(l => l.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryDwdLayerIsFlaggedForTheCapabilityCheck()
    {
        // The DWD renames products; every one of these names has to be confirmed
        // against the server rather than trusted because it once worked.
        var dwdLayers = MapLayerCatalog.All
            .Where(l => l.WmsLayers?.StartsWith("dwd:", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(dwdLayers);
        Assert.All(dwdLayers, l => Assert.True(l.NeedsCapabilityCheck, $"{l.Id} ist nicht zur Prüfung markiert"));
    }

    [Fact]
    public void TileUrlsWithASubdomainPlaceholderDeclareTheirSubdomains()
    {
        // Leaflet substitutes {s} from the subdomains option; without it the
        // literal placeholder ends up in the request and every tile fails.
        var withPlaceholder = MapLayerCatalog.All
            .Where(l => l.TileUrl?.Contains("{s}") == true)
            .ToList();

        Assert.NotEmpty(withPlaceholder);
        Assert.All(withPlaceholder, l =>
            Assert.False(string.IsNullOrWhiteSpace(l.Subdomains), $"{l.Id} deklariert keine Subdomains"));
    }

    [Fact]
    public void LayersWithoutAPlaceholderDeclareNoSubdomains()
    {
        var withoutPlaceholder = MapLayerCatalog.All
            .Where(l => l.TileUrl is not null && !l.TileUrl.Contains("{s}"));

        Assert.All(withoutPlaceholder, l => Assert.Null(l.Subdomains));
    }

    [Fact]
    public void EveryLayerIsEitherTilesOrWms()
    {
        Assert.All(MapLayerCatalog.All, l =>
            Assert.True(l.TileUrl is not null ^ l.WmsUrl is not null, $"{l.Id} ist weder Kachel- noch WMS-Layer"));
    }

    [Fact]
    public void CoarseGridOverlaysCarryAZoomCeiling()
    {
        // Radar and model products upscale into mush; each needs a ceiling.
        var coarse = MapLayerCatalog.All
            .Where(l => l.WmsLayers?.StartsWith("dwd:", StringComparison.Ordinal) == true);

        Assert.All(coarse, l => Assert.NotNull(l.MaxUsefulZoom));
    }

    [Fact]
    public void ExactlyOneBaseLayerIsEnabledByDefault()
    {
        Assert.Single(MapLayerCatalog.BaseLayers, l => l.EnabledByDefault);
    }
}
