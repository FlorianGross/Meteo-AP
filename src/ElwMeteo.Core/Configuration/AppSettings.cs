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

    /// <summary>Serial port of the NMEA receiver, e.g. "COM3". Empty disables GPS.</summary>
    public string GpsPortName { get; set; } = string.Empty;

    public int GpsBaudRate { get; set; } = 4800;

    /// <summary>Seconds between weather refreshes.</summary>
    public int WeatherRefreshSeconds { get; set; } = 300;

    /// <summary>Seconds between DWD warning refreshes.</summary>
    public int WarningRefreshSeconds { get; set; } = 300;

    /// <summary>Seconds between radar timeline refreshes.</summary>
    public int RadarRefreshSeconds { get; set; } = 300;

    /// <summary>Write a CSV row on every successful weather refresh.</summary>
    public bool CsvLoggingEnabled { get; set; } = true;

    public string CsvDirectory { get; set; } = string.Empty;

    /// <summary>Downwind extent of the hazard cone in metres; null follows the stability class.</summary>
    public double? HazardRangeMetresOverride { get; set; }

    public double HazardInnerRadiusMetres { get; set; } = 50.0;

    public bool ShowHazardCone { get; set; } = true;

    public string SelectedBaseLayerId { get; set; } = "osm";

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

    /// <summary>Keep the window above other applications — usual choice on a vehicle screen.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>Extra scaling for the whole UI, for readability at arm's length.</summary>
    public double UiScale { get; set; } = 1.0;

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

    /// <summary>Directory the CSV log is written to, resolving the empty default.</summary>
    public string ResolveCsvDirectory() =>
        string.IsNullOrWhiteSpace(CsvDirectory)
            ? Path.Combine(DefaultDirectory, "Protokoll")
            : CsvDirectory;
}
