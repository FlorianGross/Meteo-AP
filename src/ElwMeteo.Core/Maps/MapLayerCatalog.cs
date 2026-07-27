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

    /// <summary>XYZ tile template, for tile layers.</summary>
    public string? TileUrl { get; init; }

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
    /// Highest zoom at which this layer still carries information. Above it the
    /// map hides the layer and names it, instead of showing upscaled mush or an
    /// error tile the server bakes into the image. Null means no limit.
    ///
    /// Radar and model products are the ones that need this: their source grids
    /// are kilometres wide, so there is nothing to reveal by zooming further.
    /// </summary>
    public int? MaxUsefulZoom { get; init; }

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

    private const string DwdAttribution = "&copy; Deutscher Wetterdienst (GeoNutzV)";

    public static IReadOnlyList<MapLayerDefinition> All { get; } =
    [
        // ---------------------------------------------------------------- base
        new MapLayerDefinition
        {
            Id = "osm",
            Title = "OpenStreetMap",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            Attribution = "&copy; OpenStreetMap-Mitwirkende",
            MaxZoom = 19,
            EnabledByDefault = true,
            Description = "Standardkarte mit Straßennamen und Hausnummern."
        },
        new MapLayerDefinition
        {
            Id = "osm-de",
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
            Title = "OSM Humanitarian",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://tile-a.openstreetmap.fr/hot/{z}/{x}/{y}.png",
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
            TileUrl = "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, &copy; CARTO",
            MaxZoom = 19,
            Description = "Dunkle Karte — blendet nachts im Fahrzeug nicht und hebt farbige Overlays hervor."
        },
        new MapLayerDefinition
        {
            Id = "topo",
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
            Title = "CyclOSM (Wege)",
            Kind = MapLayerKind.Base,
            Group = "Karte",
            TileUrl = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png",
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
            Attribution = DwdAttribution,
            Opacity = 0.5,
            MaxUsefulZoom = 9,
            Description = "Wärme- bzw. Kältebelastung der Bevölkerung nach DWD-Modell."
        },
        new MapLayerDefinition
        {
            Id = "owm-wind",
            Title = "Windfeld",
            Kind = MapLayerKind.Overlay,
            Group = "Wind",
            WmsUrl = DwdWms,
            WmsLayers = "dwd:Windboeen",
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
            Title = "Bahnanlagen",
            Kind = MapLayerKind.Overlay,
            Group = "Infrastruktur",
            TileUrl = "https://a.tiles.openrailwaymap.org/standard/{z}/{x}/{y}.png",
            Attribution = "&copy; OpenStreetMap-Mitwirkende, OpenRailwayMap (CC-BY-SA)",
            Opacity = 0.85,
            MaxZoom = 19,
            Description = "Strecken, Betriebsstellen und Kilometrierung — für Bahnunfälle und Zugänge zur Trasse."
        },
        new MapLayerDefinition
        {
            Id = "hiking",
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
}
