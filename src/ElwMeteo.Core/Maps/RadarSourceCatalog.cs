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

    /// <summary>Only the animated source has a usable timeline.</summary>
    public bool SupportsTimeline => Kind == RadarSourceKind.RainViewerFrames;
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
            Id = "dwd-radolan",
            Title = "DWD-Niederschlagsradar (amtlich)",
            Kind = RadarSourceKind.Wms,
            Description = "Amtliches RADOLAN-Komposit des DWD. Kein Zeitverlauf, dafür die Referenz " +
                          "für Deutschland — der Layername wird beim Server geprüft.",
            Attribution = "&copy; Deutscher Wetterdienst (GeoNutzV)",
            WmsUrl = "https://maps.dwd.de/geoserver/dwd/wms",
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
            WmsLayers = "dwd:FX-Produkt",
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
