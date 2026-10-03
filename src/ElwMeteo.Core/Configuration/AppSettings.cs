using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElwMeteo.Core.Configuration;

public enum LocationMode
{
    /// <summary>Try GPS, then fall back to IP, then to the stored home position.</summary>
    Automatic,
    /// <summary>Only ever use the NMEA receiver on the configured serial port.</summary>
    GpsOnly,
    /// <summary>Use the coordinates the operator entered.</summary>
    Manual
}

/// <summary>A web viewer the operator added themselves.</summary>
public sealed class CustomWebSource
{
    public string Name { get; set; } = string.Empty;

    /// <summary>May contain {lat}, {lon} and {zoom}.</summary>
    public string UrlTemplate { get; set; } = string.Empty;
}

/// <summary>
/// User settings, persisted as JSON next to the application data. Deliberately a
/// mutable class with defaults for every member so an older or hand-edited file
/// still loads.
/// </summary>
public sealed class AppSettings
{
    public LocationMode LocationMode { get; set; } = LocationMode.Automatic;

    public double ManualLatitude { get; set; } = 50.1109;

    public double ManualLongitude { get; set; } = 8.6821;

    /// <summary>Standing location, e.g. the fire station — used as the last fallback.</summary>
    public double HomeLatitude { get; set; } = 50.1109;

    public double HomeLongitude { get; set; } = 8.6821;

    public string HomeName { get; set; } = "Feuerwache";

    /// <summary>
    /// Ask the operating system for the position when no GPS fix is available.
    /// On by default: a machine with a built-in GNSS chip already knows where it
    /// is, and the alternative rung on that ladder is the IP lookup.
    /// </summary>
    public bool UseSystemLocation { get; set; } = true;

    /// <summary>Serial port of the NMEA receiver, e.g. "COM3". Empty disables GPS.</summary>
    public string GpsPortName { get; set; } = string.Empty;

    public int GpsBaudRate { get; set; } = 4800;

    /// <summary>Seconds between weather refreshes.</summary>
    public int WeatherRefreshSeconds { get; set; } = 300;

    // There is deliberately no second interval for warnings or for the radar.
    // Both are fetched in the same pass as the weather, which is what keeps the
    // panel internally consistent: two intervals would put two different data
    // ages on one screen and leave the operator to work out which number the
    // wind arrow belongs to.

    /// <summary>Write a CSV row on every successful weather refresh.</summary>
    public bool CsvLoggingEnabled { get; set; } = true;

    public string CsvDirectory { get; set; } = string.Empty;

    /// <summary>Downwind extent of the hazard cone in metres; null follows the stability class.</summary>
    public double? HazardRangeMetresOverride { get; set; }

    public double HazardInnerRadiusMetres { get; set; } = 50.0;

    public bool ShowHazardCone { get; set; } = true;

    /// <summary>
    /// Id from <see cref="Maps.MapLayerCatalog"/>. Read through
    /// <c>MapLayerCatalog.ResolveBaseLayerId</c> rather than directly: a settings
    /// file written by an older version can name a layer that no longer exists.
    /// </summary>
    public string SelectedBaseLayerId { get; set; } = Maps.MapLayerCatalog.DefaultBaseLayerId;

    public List<string> EnabledOverlayIds { get; set; } = ["dwd-warnungen"];

    /// <summary>Radar animation frame duration in milliseconds.</summary>
    public int RadarFrameDelayMs { get; set; } = 450;

    /// <summary>Id from <see cref="Maps.RadarSourceCatalog"/>.</summary>
    public string RadarSourceId { get; set; } = "rainviewer";

    /// <summary>API key for radar sources that need one; empty disables them.</summary>
    public string OpenWeatherMapApiKey { get; set; } = string.Empty;

    /// <summary>RainViewer colour ramp id; 4 is the Weather Channel scheme.</summary>
    public int RadarColourScheme { get; set; } = 4;

    /// <summary>Render snow in its own colour rather than as rain.</summary>
    public bool RadarShowSnow { get; set; } = true;

    /// <summary>Show the infrared satellite layer beneath the radar.</summary>
    public bool ShowSatellite { get; set; }

    /// <summary>Draw the grid of wind arrows around the position.</summary>
    public bool ShowWindField { get; set; }

    /// <summary>Animate the wind field as drifting particles.</summary>
    public bool ShowWindAnimation { get; set; }

    /// <summary>
    /// Highest zoom at which radar tiles are drawn. Composites hold roughly a
    /// kilometre per pixel, so past this the picture is upscaled mush.
    /// </summary>
    public int RadarMaxZoom { get; set; } = 11;

    /// <summary>
    /// Highest zoom at which the wind animation is drawn. Beyond it the view fits
    /// inside a single grid cell and every particle carries the same vector.
    /// </summary>
    public int WindAnimationMaxZoom { get; set; } = 13;

    /// <summary>Nodes per side of the wind grid (2–9).</summary>
    public int WindFieldGridSize { get; set; } = 5;

    /// <summary>Distance between wind grid nodes in metres.</summary>
    public double WindFieldSpacingMetres { get; set; } = 2000;

    public double MapZoom { get; set; } = 11;

    // ------------------------------------------------- embedded web viewers

    /// <summary>Ids of the built-in web viewers to show; empty means all of them.</summary>
    public List<string> EnabledWebSourceIds { get; set; } = [];

    /// <summary>Viewer selected when the browser tab opens.</summary>
    public string SelectedWebSourceId { get; set; } = "rainviewer-web";

    /// <summary>
    /// Extra viewers the operator added. Persisted as name/URL pairs so a
    /// service can be swapped without a new build.
    /// </summary>
    public List<CustomWebSource> CustomWebSources { get; set; } = [];

    /// <summary>
    /// Refuse requests to advertising and tracking networks in the embedded
    /// browser. On by default: on a metered cellular link those requests are a
    /// real share of the bytes, and a banner that pushes the radar below the
    /// fold is worse on a vehicle screen than on a desk.
    ///
    /// Only third-party networks are affected — never a weather provider's own
    /// domain, and never a consent dialogue.
    /// </summary>
    public bool BlockWebTrackers { get; set; } = true;

    /// <summary>Zoom passed to viewers whose URL carries a {zoom} placeholder.</summary>
    public int WebSourceZoom { get; set; } = 9;

    // ------------------------------------------------------ auto update

    /// <summary>
    /// Look for a newer release by itself. Only ever looks — downloading and
    /// installing always need a click.
    /// </summary>
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>Hours between automatic checks; GitHub allows 60 requests/hour unauthenticated.</summary>
    public int UpdateCheckIntervalHours { get; set; } = 24;

    /// <summary>Repository the packages come from, as owner/name.</summary>
    public string UpdateRepository { get; set; } = "FlorianGross/ELW-Meteo";

    /// <summary>Offer pre-releases too. Off by default — a vehicle is not a test bench.</summary>
    public bool UpdateIncludePreReleases { get; set; }

    /// <summary>When the last check ran, so a restart does not trigger a new one.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>A version the operator dismissed; it is not offered again.</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;

    // ------------------------------------------------- warnings and alerts

    /// <summary>
    /// Sound and flash when a warning arrives that was not there before. On by
    /// default: an operations vehicle's screen is not watched continuously, and
    /// a warning nobody notices is one that was not delivered.
    /// </summary>
    public bool WarningAlertEnabled { get; set; } = true;

    /// <summary>
    /// Lowest level worth interrupting somebody for. Level 2 by default —
    /// alerting on every yellow warning is how people learn to ignore the alert.
    /// </summary>
    public Models.WarningLevel AlertMinimumLevel { get; set; } = Models.WarningLevel.Moderate;

    /// <summary>Also fetch civil-protection warnings from NINA (MoWaS, KATWARN, BIWAPP, flooding).</summary>
    public bool NinaEnabled { get; set; }

    /// <summary>Regional key of the district or city NINA is queried for.</summary>
    public string NinaArs { get; set; } = string.Empty;

    /// <summary>Plain name of that region, so the settings page can show what was picked.</summary>
    public string NinaRegionName { get; set; } = string.Empty;

    // ------------------------------------------------------------ reports

    /// <summary>Where printable reports are written; empty uses the default folder.</summary>
    public string ReportDirectory { get; set; } = string.Empty;

    /// <summary>Directory reports are written to, resolving the empty default.</summary>
    public string ResolveReportDirectory() =>
        string.IsNullOrWhiteSpace(ReportDirectory)
            ? Path.Combine(DefaultDirectory, "Berichte")
            : ReportDirectory;

    /// <summary>Keep the window above other applications — usual choice on a vehicle screen.</summary>
    public bool AlwaysOnTop { get; set; }

    // There is deliberately no global interface scale. Readability at arm's
    // length is a real need on a vehicle screen, but a single scale factor over
    // the whole window is the wrong instrument for it: the shell declares
    // MinWidth 1080, and at 1.5 the content demands 1620 logical pixels — on a
    // 1920-wide monitor at the 150% Windows scaling such screens normally run,
    // the window has 1280 to give. The result is a clipped header, not larger
    // type. Bigger lettering has to come from the styles, where the header can
    // reflow and the hit targets can grow with it.

    // ------------------------------------------------- vehicle / kiosk mode

    /// <summary>
    /// Register the application to start with the session. Off by default:
    /// something that puts itself in the autostart without being asked is a
    /// thing people uninstall.
    /// </summary>
    public bool AutostartEnabled { get; set; }

    /// <summary>Open maximised and without window decoration.</summary>
    public bool StartFullScreen { get; set; }

    /// <summary>
    /// Display the window is moved to at startup, by output name — not by index,
    /// which changes between reboots. Empty uses whichever the system calls the
    /// primary one.
    /// </summary>
    public string PreferredScreenId { get; set; } = string.Empty;

    /// <summary>Rotate through the configured views while nobody is operating the app.</summary>
    public bool CarouselEnabled { get; set; }

    /// <summary>Seconds each view is shown.</summary>
    public int CarouselIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Seconds of quiet after an interaction before the rotation resumes. Long
    /// enough to click through a few tabs by hand without being interrupted.
    /// </summary>
    public int CarouselIdleGraceSeconds { get; set; } = 60;

    /// <summary>
    /// Ids from <see cref="Kiosk.KioskStationCatalog"/>, in rotation order.
    /// Empty falls back to the default rotation.
    /// </summary>
    public List<string> CarouselStationIds { get; set; } = [];

    // ------------------------------------------------------------------ I/O

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Default location of the settings file under %APPDATA%.</summary>
    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ELW-Meteo");

    /// <summary>
    /// Loads settings, falling back to defaults when the file is missing or
    /// unreadable. A corrupt settings file must never keep the app from starting.
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path = path ?? DefaultPath;
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    /// <summary>
    /// The rotation as stations, resolving the empty default and dropping ids
    /// that no longer exist.
    /// </summary>
    public IReadOnlyList<Kiosk.KioskStation> ResolveCarouselStations()
    {
        IReadOnlyList<Kiosk.KioskStation> configured =
            Kiosk.KioskStationCatalog.Resolve(CarouselStationIds);

        return configured.Count > 0
            ? configured
            : Kiosk.KioskStationCatalog.Resolve(Kiosk.KioskStationCatalog.DefaultRotation);
    }

    /// <summary>Directory the CSV log is written to, resolving the empty default.</summary>
    public string ResolveCsvDirectory() =>
        string.IsNullOrWhiteSpace(CsvDirectory)
            ? Path.Combine(DefaultDirectory, "Protokoll")
            : CsvDirectory;
}
