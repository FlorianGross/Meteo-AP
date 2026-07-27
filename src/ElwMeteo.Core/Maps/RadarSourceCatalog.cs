namespace ElwMeteo.Core.Maps;

public enum RadarSourceKind
{
    /// <summary>Animated tile frames from the RainViewer index.</summary>
    RainViewerFrames,
    /// <summary>A WMS layer showing the provider's current composite.</summary>
    Wms,
    /// <summary>An XYZ tile service that needs a user-supplied API key.</summary>
    KeyedTiles
}

/// <summary>A selectable source for the precipitation radar on the map.</summary>
public sealed record RadarSourceDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required RadarSourceKind Kind { get; init; }

    public required string Description { get; init; }

    public required string Attribution { get; init; }

    /// <summary>Highest zoom the product still resolves.</summary>
    public int MaxUsefulZoom { get; init; } = 11;

    // --- WMS sources -----------------------------------------------------
    public string? WmsUrl { get; init; }

    public string? WmsLayers { get; init; }

    // --- keyed tile sources ---------------------------------------------
    /// <summary>Template with {z}/{x}/{y} and {key} for the API key.</summary>
    public string? TileUrlTemplate { get; init; }

    /// <summary>True when the source cannot be used without a key from the operator.</summary>
    public bool RequiresApiKey => Kind == RadarSourceKind.KeyedTiles;

    /// <summary>
    /// Candidate layer names, best first. The DWD renames products, so a source
    /// offers alternatives and the capability check picks the one the server
    /// actually advertises.
    /// </summary>
    public IReadOnlyList<string> WmsLayerCandidates { get; init; } = [];

    /// <summary>
    /// True when the layer publishes a TIME dimension and can therefore be
    /// animated through GetMap requests rather than showing a single still.
    /// </summary>
    public bool UsesTimeDimension { get; init; }

    /// <summary>
    /// Whether this source can drive the timeline. RainViewer ships explicit
    /// frames; a time-enabled WMS layer is animated through its TIME dimension.
    /// </summary>
    public bool SupportsTimeline =>
        Kind == RadarSourceKind.RainViewerFrames || UsesTimeDimension;
}

/// <summary>
/// The radar sources the map offers.
///
/// Two independent products matter operationally: RainViewer animates and
/// forecasts, the DWD composite is the official German reference. Being able to
/// switch turns "the radar looks odd" into a question that can be answered by
/// comparing two sources instead of trusting one.
/// </summary>
public static class RadarSourceCatalog
{
    public static IReadOnlyList<RadarSourceDefinition> All { get; } =
    [
        new RadarSourceDefinition
        {
            Id = "rainviewer",
            Title = "RainViewer (animiert, mit Nowcast)",
            Kind = RadarSourceKind.RainViewerFrames,
            Description = "Weltweite Komposite, rund zwei Stunden Messung plus 30 Minuten Vorhersage. " +
                          "Einzige Quelle mit Zeitleiste und Abspielfunktion.",
            Attribution = "RainViewer",
            MaxUsefulZoom = 11
        },
        new RadarSourceDefinition
        {
            Id = "dwd-wn",
            Title = "DWD-Radar mit Vorhersage (amtlich, animiert)",
            Kind = RadarSourceKind.Wms,
            Description = "Amtliches Radarkomposit des DWD samt Extrapolation über zwei Stunden, " +
                          "in 5-Minuten-Schritten und alle fünf Minuten aktualisiert. Die Zeitschritte " +
                          "kommen aus der TIME-Dimension des Servers — feiner als jeder Kacheldienst " +
                          "und die amtliche Quelle für Deutschland.",
            Attribution = "&copy; Deutscher Wetterdienst (GeoNutzV)",
            WmsUrl = "https://maps.dwd.de/geoserver/dwd/wms",
            // Best-known name first; the capability check resolves what exists.
            WmsLayerCandidates = ["dwd:WN-Produkt", "dwd:Niederschlagsradar", "dwd:WX-Produkt"],
            WmsLayers = "dwd:WN-Produkt",
            UsesTimeDimension = true,
            MaxUsefulZoom = 11
        },
        new RadarSourceDefinition
        {
            Id = "dwd-radolan",
            Title = "DWD-Niederschlagsradar (amtlich, aktuelles Bild)",
            Kind = RadarSourceKind.Wms,
            Description = "Amtliches RADOLAN-Komposit des DWD als Momentaufnahme. " +
                          "Der Layername wird beim Server geprüft.",
            Attribution = "&copy; Deutscher Wetterdienst (GeoNutzV)",
            WmsUrl = "https://maps.dwd.de/geoserver/dwd/wms",
            WmsLayerCandidates = ["dwd:Niederschlagsradar", "dwd:RX-Produkt", "dwd:WX-Produkt"],
            WmsLayers = "dwd:Niederschlagsradar",
            MaxUsefulZoom = 11
        },
        new RadarSourceDefinition
        {
            Id = "dwd-fx",
            Title = "DWD-Radarvorhersage (2 h)",
            Kind = RadarSourceKind.Wms,
            Description = "Extrapolation des DWD-Radars über die nächsten zwei Stunden.",
            Attribution = "&copy; Deutscher Wetterdienst (GeoNutzV)",
            WmsUrl = "https://maps.dwd.de/geoserver/dwd/wms",
            WmsLayerCandidates = ["dwd:FX-Produkt", "dwd:WN-Produkt"],
            WmsLayers = "dwd:FX-Produkt",
            UsesTimeDimension = true,
            MaxUsefulZoom = 11
        },
        new RadarSourceDefinition
        {
            Id = "openweathermap",
            Title = "OpenWeatherMap (eigener Schlüssel nötig)",
            Kind = RadarSourceKind.KeyedTiles,
            Description = "Weltweite Niederschlagskacheln. Benötigt einen kostenlosen API-Schlüssel, " +
                          "der in den Einstellungen hinterlegt wird.",
            Attribution = "&copy; OpenWeatherMap",
            TileUrlTemplate = "https://tile.openweathermap.org/map/precipitation_new/{z}/{x}/{y}.png?appid={key}",
            MaxUsefulZoom = 12
        }
    ];

    public static RadarSourceDefinition Default => All[0];

    public static RadarSourceDefinition ById(string? id) =>
        All.FirstOrDefault(s => s.Id == id) ?? Default;
}
