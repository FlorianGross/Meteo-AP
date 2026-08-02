using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Core.Services;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>Tab 3 — position source, refresh intervals and logging.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly GpsSerialService _gps;
    private readonly GeocodingService _geocoding;
    private readonly IShellLauncher _shell;

    private readonly NinaWarningProvider? _nina;
    private readonly ISystemLocationProvider? _systemLocation;
    private IReadOnlyList<NinaRegion> _ninaRegions = [];

    public SettingsViewModel(
        SettingsStore store,
        GpsSerialService gps,
        GeocodingService geocoding,
        IShellLauncher shell,
        NinaWarningProvider? nina = null,
        ISystemLocationProvider? systemLocation = null)
    {
        _shell = shell;
        _nina = nina;
        _systemLocation = systemLocation;
        _store = store;
        AppSettings settings = store.Settings;
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
        _warningAlertEnabled = settings.WarningAlertEnabled;
        _alertMinimumLevel = settings.AlertMinimumLevel;
        _ninaEnabled = settings.NinaEnabled;
        _ninaRegionName = settings.NinaRegionName;
        _ninaArs = settings.NinaArs;
        _useSystemLocation = settings.UseSystemLocation;
        _systemLocationStatus = systemLocation?.StatusText ?? "Nicht verfügbar.";

        RefreshPorts();
    }

    // ------------------------------------------------------ system location

    [ObservableProperty]
    private bool _useSystemLocation;

    [ObservableProperty]
    private string _systemLocationStatus = string.Empty;

    /// <summary>Name of the underlying service, or a placeholder where there is none.</summary>
    public string SystemLocationName => _systemLocation?.Name ?? "Systemortung";

    /// <summary>False on heads without an implementation, so the section can grey out.</summary>
    public bool HasSystemLocation =>
        _systemLocation is not null && _systemLocation.State != SystemLocationState.Unsupported;

    /// <summary>
    /// Asks the system where it thinks it is and reports the answer verbatim.
    ///
    /// This is the only way to find out what a given machine will actually
    /// deliver — the same call returns five metres from a GNSS chip and tens of
    /// kilometres from an IP guess. Better to learn that while setting the
    /// vehicle up than from a position that is quietly wrong on a callout.
    /// </summary>
    [RelayCommand]
    private async Task TestSystemLocationAsync(CancellationToken cancellationToken)
    {
        if (_systemLocation is null)
        {
            SystemLocationStatus = "Auf dieser Ausgabe nicht verfügbar.";
            return;
        }

        SystemLocationStatus = "Standort wird abgefragt …";

        SystemLocationState state = await _systemLocation.RequestAccessAsync().ConfigureAwait(true);

        if (state != SystemLocationState.Allowed)
        {
            SystemLocationStatus = _systemLocation.StatusText;
            return;
        }

        try
        {
            Core.Models.GeoPosition? position =
                await _systemLocation.GetAsync(cancellationToken).ConfigureAwait(true);

            SystemLocationStatus = position is null
                ? _systemLocation.StatusText
                : $"{position.Latitude:F5} / {position.Longitude:F5} — {position.SourceLabel}" +
                  (position.Description is { Length: > 0 } how ? $" ({how})" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            SystemLocationStatus = "Abfrage abgebrochen.";
        }
    }

    /// <summary>Takes the system position into the manual coordinates.</summary>
    [RelayCommand]
    private async Task UseSystemLocationAsManualAsync(CancellationToken cancellationToken)
    {
        if (_systemLocation is null)
        {
            return;
        }

        Core.Models.GeoPosition? position =
            await _systemLocation.GetAsync(cancellationToken).ConfigureAwait(true);

        if (position is null || !position.IsPlausible)
        {
            SystemLocationStatus = _systemLocation.StatusText;
            return;
        }

        ManualLatitude = position.Latitude;
        ManualLongitude = position.Longitude;
        StatusMessage = $"Position der {SystemLocationName} übernommen ({position.SourceLabel}).";
    }

    // -------------------------------------------------- warnings and alerts

    public IReadOnlyList<Core.Models.WarningLevel> AlertLevels { get; } =
    [
        Core.Models.WarningLevel.Minor,
        Core.Models.WarningLevel.Moderate,
        Core.Models.WarningLevel.Severe,
        Core.Models.WarningLevel.Extreme
    ];

    [ObservableProperty]
    private bool _warningAlertEnabled;

    [ObservableProperty]
    private Core.Models.WarningLevel _alertMinimumLevel;

    [ObservableProperty]
    private bool _ninaEnabled;

    [ObservableProperty]
    private string _ninaRegionName = string.Empty;

    [ObservableProperty]
    private string _ninaArs = string.Empty;

    [ObservableProperty]
    private string _ninaQuery = string.Empty;

    /// <summary>Districts matching the search, for the picker.</summary>
    public ObservableCollection<NinaRegion> NinaResults { get; } = [];

    /// <summary>
    /// Looks the district up by name. The federal warning system is indexed by
    /// regional key, and nobody knows theirs by heart — so the list is fetched
    /// once and searched here.
    /// </summary>
    [RelayCommand]
    private async Task SearchNinaRegionAsync(CancellationToken cancellationToken)
    {
        if (_nina is null)
        {
            StatusMessage = "NINA ist in dieser Ausgabe nicht eingerichtet.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NinaQuery))
        {
            return;
        }

        try
        {
            StatusMessage = "Regionsliste wird geladen …";

            if (_ninaRegions.Count == 0)
            {
                _ninaRegions = await _nina.GetRegionsAsync(cancellationToken).ConfigureAwait(true);
            }

            NinaResults.Clear();
            foreach (NinaRegion region in NinaWarningProvider.Search(_ninaRegions, NinaQuery))
            {
                NinaResults.Add(region);
            }

            StatusMessage = NinaResults.Count == 0
                ? $"Keine Region zu „{NinaQuery}“ gefunden."
                : $"{NinaResults.Count} Treffer — Kreis oder kreisfreie Stadt wählen.";
        }
        catch (WarningProviderException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Suche abgebrochen.";
        }
    }

    [RelayCommand]
    private void UseNinaRegion(NinaRegion? region)
    {
        if (region is null)
        {
            return;
        }

        NinaArs = region.Ars;
        NinaRegionName = region.Name;
        NinaResults.Clear();
        Apply();
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
        _settings.WarningAlertEnabled = WarningAlertEnabled;
        _settings.AlertMinimumLevel = AlertMinimumLevel;
        _settings.NinaEnabled = NinaEnabled;
        _settings.NinaArs = NinaArs.Trim();
        _settings.NinaRegionName = NinaRegionName.Trim();
        _settings.UseSystemLocation = UseSystemLocation;

        if (_nina is not null)
        {
            _nina.Enabled = _settings.NinaEnabled;
            _nina.Ars = _settings.NinaArs;
            _nina.RegionName = _settings.NinaRegionName;
        }

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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Ordner konnte nicht angelegt werden: {ex.Message}";
            return;
        }

        if (!_shell.TryOpen(CsvDirectory, out string? error))
        {
            StatusMessage = $"Ordner konnte nicht geöffnet werden: {error}";
        }
    }

    private void Save()
    {
        // Writes everything pending from the other tabs along with it — one
        // file, one writer.
        _store.SaveNow();

        if (_store.LastError is { } error)
        {
            StatusMessage = $"Einstellungen konnten nicht gespeichert werden: {error}";
        }
    }

    /// <summary>Wired to <see cref="GpsSerialService.StatusChanged"/> by the shell.</summary>
    public void ReportGpsStatus(string status) => GpsStatus = status;
}
