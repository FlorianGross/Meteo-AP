using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.App.Services;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Services;

namespace ElwMeteo.App.ViewModels;

/// <summary>Tab 3 — position source, refresh intervals and logging.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly GpsSerialService _gps;
    private readonly GeocodingService _geocoding;

    public SettingsViewModel(AppSettings settings, GpsSerialService gps, GeocodingService geocoding)
    {
        _settings = settings;
        _gps = gps;
        _geocoding = geocoding;

        _locationMode = settings.LocationMode;
        _manualLatitude = settings.ManualLatitude;
        _manualLongitude = settings.ManualLongitude;
        _homeLatitude = settings.HomeLatitude;
        _homeLongitude = settings.HomeLongitude;
        _homeName = settings.HomeName;
        _gpsPortName = settings.GpsPortName;
        _gpsBaudRate = settings.GpsBaudRate;
        _weatherRefreshSeconds = settings.WeatherRefreshSeconds;
        _csvLoggingEnabled = settings.CsvLoggingEnabled;
        _alwaysOnTop = settings.AlwaysOnTop;
        _hazardInnerRadiusMetres = settings.HazardInnerRadiusMetres;
        _radarFrameDelayMs = settings.RadarFrameDelayMs;
        _openWeatherMapApiKey = settings.OpenWeatherMapApiKey;

        RefreshPorts();
    }

    /// <summary>Raised when a setting changed that the shell has to act on.</summary>
    public event Action? SettingsApplied;

    public IReadOnlyList<LocationMode> LocationModes { get; } =
        [LocationMode.Automatic, LocationMode.GpsOnly, LocationMode.Manual];

    public IReadOnlyList<int> BaudRates { get; } = [4800, 9600, 19200, 38400, 57600, 115200];

    public ObservableCollection<string> AvailablePorts { get; } = [];

    public ObservableCollection<PlaceResult> SearchResults { get; } = [];

    [ObservableProperty]
    private LocationMode _locationMode;

    [ObservableProperty]
    private double _manualLatitude;

    [ObservableProperty]
    private double _manualLongitude;

    [ObservableProperty]
    private double _homeLatitude;

    [ObservableProperty]
    private double _homeLongitude;

    [ObservableProperty]
    private string _homeName = string.Empty;

    [ObservableProperty]
    private string _gpsPortName = string.Empty;

    [ObservableProperty]
    private int _gpsBaudRate = 4800;

    [ObservableProperty]
    private int _weatherRefreshSeconds = 300;

    [ObservableProperty]
    private bool _csvLoggingEnabled;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private double _hazardInnerRadiusMetres = 50;

    [ObservableProperty]
    private int _radarFrameDelayMs = 450;

    /// <summary>Only needed for radar sources that require a key; otherwise empty.</summary>
    [ObservableProperty]
    private string _openWeatherMapApiKey = string.Empty;

    [ObservableProperty]
    private string _gpsStatus = "GPS nicht verbunden.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public string CsvDirectory => _settings.ResolveCsvDirectory();

    public string SettingsFilePath => AppSettings.DefaultPath;

    // ------------------------------------------------------------- commands

    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (string port in GpsSerialService.AvailablePorts())
        {
            AvailablePorts.Add(port);
        }

        if (AvailablePorts.Count == 0)
        {
            GpsStatus = "Keine seriellen Schnittstellen gefunden.";
        }
    }

    [RelayCommand]
    private void ConnectGps()
    {
        if (_gps.Open(GpsPortName, GpsBaudRate))
        {
            _settings.GpsPortName = GpsPortName;
            _settings.GpsBaudRate = GpsBaudRate;
            Save();
        }
    }

    [RelayCommand]
    private void DisconnectGps()
    {
        _gps.Close();
        GpsStatus = "GPS getrennt.";
    }

    /// <summary>Copies the last GPS fix into the manual coordinate fields.</summary>
    [RelayCommand]
    private void UseGpsFixAsManual()
    {
        if (_gps.LastFix is not { } fix)
        {
            StatusMessage = "Noch kein GPS-Fix vorhanden.";
            return;
        }

        ManualLatitude = Math.Round(fix.Latitude, 5);
        ManualLongitude = Math.Round(fix.Longitude, 5);
        StatusMessage = "GPS-Position in die manuellen Koordinaten übernommen.";
    }

    [RelayCommand]
    private void UseManualAsHome()
    {
        HomeLatitude = ManualLatitude;
        HomeLongitude = ManualLongitude;
        StatusMessage = "Manuelle Position als Standort gespeichert.";
        Apply();
    }

    [RelayCommand]
    private async Task SearchPlaceAsync(CancellationToken cancellationToken)
    {
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        try
        {
            StatusMessage = "Suche läuft …";
            IReadOnlyList<PlaceResult> results = await _geocoding
                .SearchAsync(SearchQuery, cancellationToken)
                .ConfigureAwait(true);

            foreach (PlaceResult place in results)
            {
                SearchResults.Add(place);
            }

            StatusMessage = results.Count == 0
                ? "Keine Treffer."
                : $"{results.Count} Treffer — Eintrag anklicken zum Übernehmen.";
        }
        catch (GeocodingException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Suche abgebrochen.";
        }
    }

    [RelayCommand]
    private void UsePlace(PlaceResult? place)
    {
        if (place is null)
        {
            return;
        }

        ManualLatitude = Math.Round(place.Latitude, 5);
        ManualLongitude = Math.Round(place.Longitude, 5);
        LocationMode = LocationMode.Manual;
        StatusMessage = $"Position auf {place.DisplayName} gesetzt.";
        Apply();
    }

    [RelayCommand]
    private void Apply()
    {
        _settings.LocationMode = LocationMode;
        _settings.ManualLatitude = ManualLatitude;
        _settings.ManualLongitude = ManualLongitude;
        _settings.HomeLatitude = HomeLatitude;
        _settings.HomeLongitude = HomeLongitude;
        _settings.HomeName = HomeName;
        _settings.GpsPortName = GpsPortName;
        _settings.GpsBaudRate = GpsBaudRate;
        // Below a minute the free APIs start rate-limiting; clamp rather than trust input.
        _settings.WeatherRefreshSeconds = Math.Clamp(WeatherRefreshSeconds, 60, 3600);
        _settings.WarningRefreshSeconds = _settings.WeatherRefreshSeconds;
        _settings.CsvLoggingEnabled = CsvLoggingEnabled;
        _settings.AlwaysOnTop = AlwaysOnTop;
        _settings.HazardInnerRadiusMetres = Math.Clamp(HazardInnerRadiusMetres, 10, 1000);
        _settings.RadarFrameDelayMs = Math.Clamp(RadarFrameDelayMs, 100, 3000);
        _settings.OpenWeatherMapApiKey = OpenWeatherMapApiKey.Trim();

        Save();
        SettingsApplied?.Invoke();
        StatusMessage = "Einstellungen übernommen.";
    }

    [RelayCommand]
    private void OpenCsvDirectory()
    {
        try
        {
            Directory.CreateDirectory(CsvDirectory);
            Process.Start(new ProcessStartInfo(CsvDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ordner konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    private void Save()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Einstellungen konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    /// <summary>Wired to <see cref="GpsSerialService.StatusChanged"/> by the shell.</summary>
    public void ReportGpsStatus(string status) => GpsStatus = status;
}
