using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Maps;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// Which rendering services the application is allowed to point at.
///
/// This exists because of a failure that was ours and looked like an outage.
/// The map shipped with tile.openstreetmap.org as its default base layer, and
/// the OSM Foundation's tile usage policy names that case outright: „Heavy use,
/// such as distributing an app that uses tiles from openstreetmap.org, is
/// forbidden without prior permission." The servers are volunteer-run and
/// donation-funded. What the policy produces when ignored is not a warning but a
/// wall of 403 tiles reading „Access blocked" — on a vehicle screen, in the
/// middle of an incident, with nothing on the panel explaining why.
///
/// A rule nobody can accidentally undo is better than a decision in a commit
/// message, so it is a test.
/// </summary>
public class TileServiceTests
{
    /// <summary>Hosts this application must not request map tiles from.</summary>
    public static TheoryData<string> ForbiddenTileHosts =>
    [
        // The OSM Foundation's own rendering service.
        "tile.openstreetmap.org",
        "a.tile.openstreetmap.org",
        "b.tile.openstreetmap.org",
        "c.tile.openstreetmap.org"
    ];

    [Theory]
    [MemberData(nameof(ForbiddenTileHosts))]
    public void NoLayerRequestsTilesFromAForbiddenHost(string host)
    {
        IEnumerable<string> urls = MapLayerCatalog.All
            .Select(l => l.TileUrl)
            .Concat(MapLayerCatalog.All.Select(l => l.WmsUrl))
            .Where(u => !string.IsNullOrWhiteSpace(u))!;

        Assert.DoesNotContain(urls, u => u!.Contains(host, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheDefaultBaseLayerIsAnOfficialService()
    {
        MapLayerDefinition? fallback = MapLayerCatalog.BaseLayers
            .SingleOrDefault(l => l.Id == MapLayerCatalog.DefaultBaseLayerId);

        Assert.NotNull(fallback);

        // Not a volunteer project. The default is what every installation lands
        // on without anybody choosing it, so it is the one that must not be a
        // service somebody else is paying for out of donations.
        Assert.False(fallback!.IsCommunityService);
        Assert.True(fallback.EnabledByDefault);
    }

    [Fact]
    public void AFreshInstallationStartsOnTheDefault()
    {
        AppSettings settings = new();

        Assert.Equal(
            MapLayerCatalog.DefaultBaseLayerId,
            MapLayerCatalog.ResolveBaseLayerId(settings.SelectedBaseLayerId));
    }

    [Fact]
    public void ExactlyOneBaseLayerIsOnByDefault()
    {
        // Base layers are mutually exclusive; two marked as default is a map
        // that renders one of them and nobody can say which.
        Assert.Single(MapLayerCatalog.BaseLayers, l => l.EnabledByDefault);
    }

    // ------------------------------------------------- the stored settings file

    [Fact]
    public void TheRetiredLayerIsMovedToTheDefault()
    {
        // The case that matters most: the installations already looking at a wall
        // of „Access blocked" tiles are exactly the ones carrying "osm" in their
        // settings file. Without this they would keep it after the update.
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId("osm"));
    }

    [Fact]
    public void ASettingsFileFromTheOldVersionIsMovedOn()
    {
        // End to end through the serializer, because that is how it will actually
        // arrive: a file on disk written by a version that still had the layer.
        string path = Path.Combine(Path.GetTempPath(), $"elw-osm-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """{ "SelectedBaseLayerId": "osm" }""");

            AppSettings loaded = AppSettings.Load(path);

            Assert.Equal("osm", loaded.SelectedBaseLayerId);
            Assert.Equal(
                MapLayerCatalog.DefaultBaseLayerId,
                MapLayerCatalog.ResolveBaseLayerId(loaded.SelectedBaseLayerId));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void AnExistingChoiceIsLeftAlone()
    {
        // Somebody who picked the grey official map keeps it. A migration that
        // resets everybody's choice is its own kind of broken.
        Assert.Equal("topplus", MapLayerCatalog.ResolveBaseLayerId("topplus"));
        Assert.Equal("osm-de", MapLayerCatalog.ResolveBaseLayerId("osm-de"));
    }

    [Fact]
    public void AnUnknownOrEmptyIdLandsOnTheDefault()
    {
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId(null));
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId(""));
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId("   "));
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId("gibtsnicht"));
    }

    [Fact]
    public void TheIdIsResolvedWithoutRegardForCaseOrPadding()
    {
        // Hand-edited settings files are a stated design assumption elsewhere in
        // AppSettings; they should not cost somebody their map.
        Assert.Equal("topplus", MapLayerCatalog.ResolveBaseLayerId("  TopPlus  "));
        Assert.Equal(MapLayerCatalog.DefaultBaseLayerId, MapLayerCatalog.ResolveBaseLayerId("OSM"));
    }

    [Fact]
    public void EveryRetiredLayerPointsAtOneThatExists()
    {
        // A successor that does not exist is worse than none: it resolves to a
        // layer the map cannot build, and the map comes up empty.
        foreach ((string retired, string successor) in MapLayerCatalog.RetiredBaseLayers)
        {
            Assert.DoesNotContain(MapLayerCatalog.BaseLayers, l => l.Id == retired);
            Assert.Contains(MapLayerCatalog.BaseLayers, l => l.Id == successor);
        }
    }

    // ------------------------------------------------------ community services

    [Fact]
    public void TheVolunteerRunServicesAreMarkedAsSuch()
    {
        // They stay on offer — they show things the official map does not — but an
        // operator picking one should be able to see what they are picking.
        foreach (string id in new[] { "osm-de", "osm-hot", "cyclosm", "topo" })
        {
            MapLayerDefinition layer = MapLayerCatalog.All.Single(l => l.Id == id);

            Assert.True(layer.IsCommunityService, $"{id} ist nicht als Gemeinschaftsdienst markiert.");
        }
    }

    [Fact]
    public void TheOfficialServicesAreNotMarked()
    {
        foreach (string id in new[] { "basemapde", "basemapde-grau", "topplus" })
        {
            MapLayerDefinition layer = MapLayerCatalog.All.Single(l => l.Id == id);

            Assert.False(layer.IsCommunityService, $"{id} ist fälschlich als Gemeinschaftsdienst markiert.");
        }
    }

    [Fact]
    public void EveryLayerStillHasAnAttribution()
    {
        // Attribution is a licence condition for every one of these services, not
        // a courtesy, and it is the kind of thing an edit to this catalog drops.
        Assert.All(MapLayerCatalog.All, l => Assert.False(string.IsNullOrWhiteSpace(l.Attribution)));
    }
}
