namespace ElwMeteo.Core.Maps;

public enum MapLayerKind
{
    /// <summary>Mutually exclusive background map.</summary>
    Base,
    /// <summary>Semi-transparent layer drawn on top of the base map.</summary>
    Overlay
}

/// <summary>
/// A tile or WMS layer offered on the map tab. Kept as data so the list can grow
/// without touching the map's JavaScript.
/// </summary>
public sealed record MapLayerDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required MapLayerKind Kind { get; init; }

    /// <summary>Grouping shown in the layer panel.</summary>
    public string Group { get; init; } = "Allgemein";

    /// <summary>XYZ tile template, for tile layers. May contain {s} for subdomains.</summary>
    public string? TileUrl { get; init; }

    /// <summary>
    /// Subdomains for a {s} placeholder. Several community tile services spread
    /// load across a/b/c and rate-limit or refuse a client that hammers one host.
    /// </summary>
    public string? Subdomains { get; init; }

    /// <summary>WMS base URL, for WMS layers.</summary>
    public string? WmsUrl { get; init; }

    /// <summary>Comma-separated WMS layer names.</summary>
    public string? WmsLayers { get; init; }

    public string WmsFormat { get; init; } = "image/png";

    public bool WmsTransparent { get; init; } = true;

    public required string Attribution { get; init; }

    public double Opacity { get; init; } = 1.0;

    public int MaxZoom { get; init; } = 19;

    /// <summary>Enabled the first time the map opens.</summary>
    public bool EnabledByDefault { get; init; }

    /// <summary>One-line explanation shown as a tooltip in the layer panel.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// True when the layer name still has to be confirmed against the server's
    /// capabilities document. The map panel marks these until the check has run.
    /// </summary>
    public bool NeedsCapabilityCheck { get; init; }

    /// <summary>
    /// Highest zoom at which this layer still carries information. Above it the
    /// map hides the layer and names it, instead of showing upscaled mush or an
    /// error tile the server bakes into the image. Null means no limit.
    ///
    /// Radar and model products are the ones that need this: their source grids
    /// are kilometres wide, so there is nothing to reveal by zooming further.
    /// </summary>
    public int? MaxUsefulZoom { get; init; }

    /// <summary>
    /// True for a rendering service run by volunteers on donated capacity rather
    /// than by an authority or a company.
    ///
    /// Worth marking because the failure mode is specific and unpleasant: such a
    /// service does not get slower under load, it blocks — and what the operator
    /// then sees is a map full of error tiles, usually at the worst moment. The
    /// official German services carry no such risk, which is why the default sits
    /// there. These stay on offer because they show things the official map does
    /// not, but an operator who picks one should know what they picked.
    /// </summary>
    public bool IsCommunityService { get; init; }

    public bool IsWms => !string.IsNullOrWhiteSpace(WmsUrl);
}

/// <summary>
/// The layers the map tab offers.
///
/// DWD products come from the DWD GeoServer as Open Data under the GeoNutzV.
/// Layer names there occasionally change with a product release; if an overlay
/// stays blank, compare against
/// https://maps.dwd.de/geoserver/dwd/wms?service=WMS&amp;request=GetCapabilities
/// and adjust <see cref="MapLayerDefinition.WmsLayers"/>.
/// </summary>
public static class MapLayerCatalog
{
    private const string DwdWms = "https://maps.dwd.de/geoserver/dwd/wms";

    /// <summary>The DWD GeoServer endpoint, for the runtime capability check.</summary>
    public static string DwdWmsEndpoint => DwdWms;

    private const string DwdAttribution = "&copy; Deutscher Wetterdienst (GeoNutzV)";

    public static IReadOnlyList<MapLayerDefinition> All { get; } =
    [
        // ---------------------------------------------------------------- base
        // There is deliberately no layer for tile.openstreetmap.org. The OSM
        // Foundation's tile usage policy names this case outright: „Heavy use,
        // such as distributing an app that uses tiles from openstreetmap.org, is
        // forbidden without prior permission." The servers are volunteer-run and
        // donation-funded, and an application shipped to fire services is exactly
        // the distributed use they cannot carry. What the policy produces when
        // ignored is not a warning but a wall of 403 tiles reading „Access
        // blocked" — on a vehicle screen, during an incident.
        //
        // The map data is still OpenStreetMap's in several of the layers below;
        // what cannot be used is their rendering service.
        new MapLayerDefinition
        {
            Id = "osm-de",
            IsCommunityService = true,
            Title = "OpenStreetMap.de",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://tile.openstreetmap.de/{z}/{x}/{y}.png",
            Attribution = "&copy; OpenStreetMap-Mitwirkende",
            MaxZoom = 18,
            Description = "Deutscher Stil mit deutschsprachigen Bezeichnungen."
        },
        new MapLayerDefinition
        {
            Id = "basemapde",
            Title = "basemap.de (amtlich)",
            Kind = MapLayerKind.Base,
            Group = "Amtlich (BKG)",
            // WMTS RESTful — note the {y}/{x} (row/col) order.
            TileUrl = "https://sgx.geodatenzentrum.de/wmts_basemapde/tile/1.0.0/de_basemapde_web_raster_farbe/default/GLOBAL_WEBMERCATOR/{z}/{y}/{x}.png",
            Attribution = "&copy; basemap.de / BKG",
            MaxZoom = 19,
            // The default, and not merely because the previous one had to go. This
            // is the official web map of the German surveying authorities,
            // published as open data and built to be used — including by
            // applications. It is also the map many control rooms already have on
            // the wall, which is worth something of its own when two people are
            // describing a position to each other over the radio.
            EnabledByDefault = true,
            Description = "Amtliche Web-Karte der deutschen Vermessungsverwaltungen — dieselbe Grundlage wie in vielen Leitstellen."
        },
        new MapLayerDefinition
        {
            Id = "basemapde-grau",
            Title = "basemap.de (grau)",
            Kind = MapLayerKind.Base,
            Group = "Amtlich (BKG)",
            TileUrl = "https://sgx.geodatenzentrum.de/wmts_basemapde/tile/1.0.0/de_basemapde_web_raster_grau/default/GLOBAL_WEBMERCATOR/{z}/{y}/{x}.png",
            Attribution = "&copy; basemap.de / BKG",
            MaxZoom = 19,
            Description = "Zurückhaltende Graustufen — lässt Radar und Warnflächen klar hervortreten."
        },
        new MapLayerDefinition
        {
            Id = "topplus",
            Title = "TopPlusOpen (amtlich)",
            Kind = MapLayerKind.Base,
            Group = "Amtlich (BKG)",
            TileUrl = "https://sgx.geodatenzentrum.de/wmts_topplus_open/tile/1.0.0/web/default/WEBMERCATOR/{z}/{y}/{x}.png",
            Attribution = "&copy; Bundesamt für Kartographie und Geodäsie (TopPlusOpen)",
            MaxZoom = 18,
            Description = "Amtliche topographische Karte des BKG mit Gelände und Infrastruktur."
        },
        new MapLayerDefinition
        {
            Id = "osm-hot",
            IsCommunityService = true,
            Title = "OSM Humanitarian",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://{s}.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png",
            Subdomains = "abc",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, Humanitarian OSM Team",
            MaxZoom = 19,
            Description = "Kontrastreicher Stil für Einsatzlagen — betont Wege, Wasser und Infrastruktur."
        },
        new MapLayerDefinition
        {
            Id = "carto-dark",
            Title = "Dunkel (nachttauglich)",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            Subdomains = "abcd",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, &copy; CARTO",
            MaxZoom = 19,
            Description = "Dunkle Karte — blendet nachts im Fahrzeug nicht und hebt farbige Overlays hervor."
        },
        new MapLayerDefinition
        {
            Id = "topo",
            IsCommunityService = true,
            Title = "OpenTopoMap (Gelände)",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://tile.opentopomap.org/{z}/{x}/{y}.png",
            Attribution = "Kartendaten: &copy; OpenStreetMap-Mitwirkende, SRTM | Darstellung: &copy; OpenTopoMap (CC-BY-SA)",
            MaxZoom = 17,
            Description = "Höhenlinien und Geländeformen — hilfreich bei Vegetationsbränden und Suchmaßnahmen."
        },
        new MapLayerDefinition
        {
            Id = "cyclosm",
            IsCommunityService = true,
            Title = "CyclOSM (Wege)",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://{s}.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png",
            Subdomains = "abc",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, CyclOSM",
            MaxZoom = 18,
            Description = "Betont Wirtschaftswege und Pfade — nützlich für Zugänge abseits der Straße."
        },
        new MapLayerDefinition
        {
            Id = "esri-imagery",
            Title = "Luftbild",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            Attribution = "Luftbild: Esri, Maxar, Earthstar Geographics",
            MaxZoom = 18,
            Description = "Satelliten- und Luftbild — zeigt Bebauung, Hallen, Lagerflächen und Zufahrten."
        },

        // ------------------------------------------------------------- overlay
        new MapLayerDefinition
        {
            Id = "dwd-warnungen",
            Title = "DWD-Warngebiete",
            Kind = MapLayerKind.Overlay,
            Group = "Warnungen",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Warnungen_Gemeinden",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.55,
            EnabledByDefault = true,
            MaxUsefulZoom = 13,
            Description = "Amtliche Warnungen des DWD auf Gemeindeebene."
        },
        new MapLayerDefinition
        {
            Id = "dwd-warnungen-kreise",
            Title = "DWD-Warngebiete (Landkreise)",
            Kind = MapLayerKind.Overlay,
            Group = "Warnungen",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Warnungen_Landkreise_Gemeinden_vereinigt",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.5,
            MaxUsefulZoom = 12,
            Description = "Gröbere Übersicht über die Warnlage in der Region."
        },
        new MapLayerDefinition
        {
            Id = "dwd-radar",
            Title = "DWD-Niederschlagsradar",
            Kind = MapLayerKind.Overlay,
            Group = "Niederschlag",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Niederschlagsradar",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.7,
            MaxUsefulZoom = 11,
            Description = "Amtliches RADOLAN-Radarkomposit — die Referenz für Deutschland."
        },
        new MapLayerDefinition
        {
            Id = "dwd-radar-fx",
            Title = "DWD-Radarvorhersage (2 h)",
            Kind = MapLayerKind.Overlay,
            Group = "Niederschlag",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:FX-Produkt",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.7,
            MaxUsefulZoom = 11,
            Description = "Extrapolierte Radarvorhersage der nächsten zwei Stunden."
        },
        new MapLayerDefinition
        {
            Id = "dwd-wbi",
            Title = "Waldbrandgefahrenindex",
            Kind = MapLayerKind.Overlay,
            Group = "Vegetationsbrand",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Waldbrandgefahrenindex",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.55,
            MaxUsefulZoom = 10,
            Description = "Amtlicher WBI des DWD (Stufe 1-5)."
        },
        new MapLayerDefinition
        {
            Id = "dwd-grasland",
            Title = "Graslandfeuerindex",
            Kind = MapLayerKind.Overlay,
            Group = "Vegetationsbrand",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Graslandfeuerindex",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.55,
            MaxUsefulZoom = 10,
            Description = "Gefahrenindex für Feuer in offenem Grasland."
        },
        new MapLayerDefinition
        {
            Id = "dwd-gefuehlte-temp",
            Title = "Gefühlte Temperatur",
            Kind = MapLayerKind.Overlay,
            Group = "Belastung",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:GefuehlteTemperatur",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.5,
            MaxUsefulZoom = 9,
            Description = "Wärme- bzw. Kältebelastung der Bevölkerung nach DWD-Modell."
        },
        new MapLayerDefinition
        {
            Id = "dwd-windboeen",
            Title = "Windböen (Modell)",
            Kind = MapLayerKind.Overlay,
            Group = "Wind",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Windboeen",
            NeedsCapabilityCheck = true,
            Attribution = DwdAttribution,
            Opacity = 0.5,
            MaxUsefulZoom = 9,
            Description = "Modellierte Windböen aus dem ICON-Modell."
        },
        new MapLayerDefinition
        {
            Id = "hillshade",
            Title = "Schummerung (Relief)",
            Kind = MapLayerKind.Overlay,
            Group = "Gelände",
            // The old Wikimedia Labs hillshading service is retired; Esri's
            // World Hillshade is the drop-in replacement and note the {y}/{x} order.
            TileUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/Elevation/World_Hillshade/MapServer/tile/{z}/{y}/{x}",
            Attribution = "Schummerung: Esri, USGS, NOAA",
            Opacity = 0.45,
            MaxZoom = 16,
            Description = "Reliefschattierung zur Beurteilung von Hanglagen."
        },
        new MapLayerDefinition
        {
            Id = "seamarks",
            IsCommunityService = true,
            Title = "Gewässer / Seezeichen",
            Kind = MapLayerKind.Overlay,
            Group = "Infrastruktur",
            TileUrl = "https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png",
            Attribution = "&copy; OpenSeaMap-Mitwirkende",
            Opacity = 0.9,
            MaxZoom = 18,
            Description = "Seezeichen und Wasserinfrastruktur für Einsätze am Wasser."
        },
        new MapLayerDefinition
        {
            Id = "railway",
            IsCommunityService = true,
            Title = "Bahnanlagen",
            Kind = MapLayerKind.Overlay,
            Group = "Infrastruktur",
            TileUrl = "https://{s}.tiles.openrailwaymap.org/standard/{z}/{x}/{y}.png",
            Subdomains = "abc",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, OpenRailwayMap (CC-BY-SA)",
            Opacity = 0.85,
            MaxZoom = 19,
            Description = "Strecken, Betriebsstellen und Kilometrierung — für Bahnunfälle und Zugänge zur Trasse."
        },
        new MapLayerDefinition
        {
            Id = "hiking",
            IsCommunityService = true,
            Title = "Wanderwege",
            Kind = MapLayerKind.Overlay,
            Group = "Gelände",
            TileUrl = "https://tile.waymarkedtrails.org/hiking/{z}/{x}/{y}.png",
            Attribution = "&copy; waymarkedtrails.org, OpenStreetMap-Mitwirkende (CC-BY-SA)",
            Opacity = 0.8,
            MaxZoom = 18,
            Description = "Markierte Wege im Gelände — Anhaltspunkte für Suchmaßnahmen und Zufahrten."
        }
    ];

    public static IEnumerable<MapLayerDefinition> BaseLayers => All.Where(l => l.Kind == MapLayerKind.Base);

    public static IEnumerable<MapLayerDefinition> Overlays => All.Where(l => l.Kind == MapLayerKind.Overlay);

    /// <summary>The base map a fresh installation starts on.</summary>
    public static string DefaultBaseLayerId => "basemapde";

    /// <summary>
    /// Base layers that were offered once and are not any more, so a settings
    /// file written by an older version can be moved on rather than silently
    /// landing nowhere.
    ///
    /// <c>osm</c> is here because shipping it was against the OSM Foundation's
    /// tile usage policy, and the installations that already carry it in their
    /// settings file are precisely the ones looking at a wall of „Access
    /// blocked" tiles. Dropping the layer without this would leave them on an
    /// id that no longer resolves — which, depending on where it is read, is
    /// either an empty map or the first entry of a list, neither of them a
    /// decision anybody made.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RetiredBaseLayers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["osm"] = DefaultBaseLayerId
        };

    /// <summary>
    /// The base layer to actually use for a stored id: itself when it still
    /// exists, its successor when it was retired, the default otherwise.
    /// </summary>
    public static string ResolveBaseLayerId(string? storedId)
    {
        if (string.IsNullOrWhiteSpace(storedId))
        {
            return DefaultBaseLayerId;
        }

        string trimmed = storedId.Trim();

        // The catalog's own spelling, not the stored one. Callers compare the
        // result against MapLayerDefinition.Id with an ordinal comparison, so
        // handing back "TopPlus" from a hand-edited file would match nothing and
        // leave the map blank — the opposite of being tolerant about it.
        MapLayerDefinition? known = BaseLayers.FirstOrDefault(
            l => string.Equals(l.Id, trimmed, StringComparison.OrdinalIgnoreCase));

        if (known is not null)
        {
            return known.Id;
        }

        return RetiredBaseLayers.TryGetValue(trimmed, out string? successor)
            ? successor
            : DefaultBaseLayerId;
    }
}
