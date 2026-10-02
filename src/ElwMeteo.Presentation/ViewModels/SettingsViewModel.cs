using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Kiosk;
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
    private readonly IAutostartService _autostart;
    private readonly IScreenService _screenService;
    private IReadOnlyList<NinaRegion> _ninaRegions = [];

    public SettingsViewModel(
        SettingsStore store,
        GpsSerialService gps,
        GeocodingService geocoding,
        IShellLauncher shell,
        NinaWarningProvider? nina = null,
        ISystemLocationProvider? systemLocation = null,
        IAutostartService? autostart = null,
        IScreenService? screens = null)
    {
        _autostart = autostart ?? new UnsupportedAutostartService(
            "Automatischer Start ist in dieser Umgebung nicht verfügbar.");
        _screenService = screens ?? new NoScreenService();
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
        _blockWebTrackers = settings.BlockWebTrackers;
        _reportDirectory = settings.ResolveReportDirectory();
        _systemLocationStatus = systemLocation?.StatusText ?? "Nicht verfügbar.";

        _startFullScreen = settings.StartFullScreen;
        _carouselEnabled = settings.CarouselEnabled;
        _carouselIntervalSeconds = settings.CarouselIntervalSeconds;
        _carouselIdleGraceSeconds = settings.CarouselIdleGraceSeconds;
        _preferredScreenId = settings.PreferredScreenId;

        // Read back rather than trusting the settings file: somebody may have
        // removed the entry with msconfig or a cleanup tool, and the checkbox has
        // to say what is actually registered.
        _autostartEnabled = _autostart.IsEnabled();
        _autostartLocation = _autostart.Describe();

        IReadOnlyList<KioskStation> rotation = settings.ResolveCarouselStations();

        foreach (KioskStation station in KioskStationCatalog.All)
        {
            CarouselStations.Add(new KioskStationOption(station)
            {
                IsIncluded = rotation.Any(s => s.Id == station.Id)
            });
        }

        RefreshPorts();
        RefreshScreens();
    }

    // ------------------------------------------------- vehicle / kiosk mode

    /// <summary>Whether this build can register an autostart entry at all.</summary>
    public bool AutostartSupported => _autostart.IsSupported;

    [ObservableProperty]
    private bool _autostartEnabled;

    /// <summary>Registry path or file the entry goes to, shown so it can be checked.</summary>
    [ObservableProperty]
    private string _autostartLocation = string.Empty;

    [ObservableProperty]
    private bool _startFullScreen;

    [ObservableProperty]
    private bool _carouselEnabled;

    [ObservableProperty]
    private int _carouselIntervalSeconds;

    [ObservableProperty]
    private int _carouselIdleGraceSeconds;

    /// <summary>Output name of the chosen display; empty means the primary one.</summary>
    [ObservableProperty]
    private string _preferredScreenId = string.Empty;

    /// <summary>The displays to pick from. The first entry is "primary", id empty.</summary>
    public ObservableCollection<ScreenInfo> AvailableScreens { get; } = [];

    /// <summary>Every station the rotation can include, with its checkbox state.</summary>
    public ObservableCollection<KioskStationOption> CarouselStations { get; } = [];

    /// <summary>
    /// Re-reads the attached displays. Bound to a button rather than done
    /// continuously: a vehicle gets a monitor plugged in while the application is
    /// already running, and the settings page should pick that up without a
    /// restart.
    /// </summary>
    [RelayCommand]
    private void RefreshScreens()
    {
        string previous = PreferredScreenId;

        AvailableScreens.Clear();

        // Sentinel for "whatever the system calls primary", so the common case
        // needs no knowledge of connector names.
        AvailableScreens.Add(new ScreenInfo(
            string.Empty, "Hauptbildschirm (automatisch)", 0, 0, 0, 0, true));

        foreach (ScreenInfo screen in _screenService.List())
        {
            AvailableScreens.Add(screen);
        }

        // Keep a configured display selected even when it is not connected right
        // now, so opening the settings page does not quietly reset the choice.
        PreferredScreenId = previous;

        ScreenNotice = ScreenChoice.IsPreferenceMissing(_screenService.List(), previous)
            ? $"Bildschirm „{previous}“ ist derzeit nicht angeschlossen."
            : string.Empty;
    }

    [ObservableProperty]
    private string _screenNotice = string.Empty;

    // ---------------------------------------------------------- web filter

    [ObservableProperty]
    private bool _blockWebTrackers;

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

    /// <summary>
    /// Where printable reports are written. Editable, unlike the log folder
    /// above: a report is the thing that gets carried off the vehicle, so the
    /// usual reason to change it is a stick or a share that is mounted now.
    /// </summary>
    [ObservableProperty]
    private string _reportDirectory = string.Empty;

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
        _settings.BlockWebTrackers = BlockWebTrackers;
        _settings.ReportDirectory = NormaliseReportDirectory(ReportDirectory);
        _settings.StartFullScreen = StartFullScreen;
        _settings.PreferredScreenId = PreferredScreenId?.Trim() ?? string.Empty;
        _settings.CarouselEnabled = CarouselEnabled;
        _settings.CarouselIntervalSeconds =
            (int)TabCarousel.ClampInterval(CarouselIntervalSeconds).TotalSeconds;
        _settings.CarouselIdleGraceSeconds =
            (int)TabCarousel.ClampIdleGrace(CarouselIdleGraceSeconds).TotalSeconds;
        _settings.CarouselStationIds =
            [.. CarouselStations.Where(o => o.IsIncluded).Select(o => o.Station.Id)];

        ApplyAutostart();

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

    /// <summary>
    /// Stores the empty string when the box still holds the default folder, so a
    /// page that was merely opened and applied does not silently pin the path.
    /// The default would then stop following the application data folder, and
    /// nothing would say why reports kept landing in the old place.
    /// </summary>
    private static string NormaliseReportDirectory(string? entered)
    {
        string trimmed = (entered ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        string fallback = new AppSettings { ReportDirectory = string.Empty }.ResolveReportDirectory();

        return string.Equals(
            trimmed.TrimEnd('/', '\\'),
            fallback.TrimEnd('/', '\\'),
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : trimmed;
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

    /// <summary>
    /// Writes or removes the autostart entry to match the checkbox, and reports
    /// a refusal in the status line instead of throwing. The checkbox is then set
    /// back to what is actually registered — a tick that stayed on after the
    /// write failed would be a lie the operator only finds out about after the
    /// next reboot, which is to say at the next incident.
    /// </summary>
    private void ApplyAutostart()
    {
        if (!_autostart.IsSupported)
        {
            return;
        }

        if (AutostartEnabled == _autostart.IsEnabled())
        {
            return;
        }

        if (!_autostart.TrySet(AutostartEnabled, out string? error))
        {
            StatusMessage = $"Automatischer Start konnte nicht gesetzt werden: {error}";
            AutostartEnabled = _autostart.IsEnabled();
            return;
        }

        _settings.AutostartEnabled = AutostartEnabled;
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

/// <summary>
/// A rotation station with its checkbox state, so the settings page can bind to
/// something that notifies. The station itself is an immutable record from the
/// domain library; this is only the selection on top of it.
/// </summary>
public sealed partial class KioskStationOption(ElwMeteo.Core.Kiosk.KioskStation station) : ObservableObject
{
    public ElwMeteo.Core.Kiosk.KioskStation Station { get; } = station;

    public string Title => Station.Title;

    [ObservableProperty]
    private bool _isIncluded;
}
