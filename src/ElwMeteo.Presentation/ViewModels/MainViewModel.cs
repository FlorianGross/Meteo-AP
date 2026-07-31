using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Services;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>
/// Application shell: owns the three tabs, the shared clock and the auto-refresh
/// schedule.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly GpsSerialService _gps;
    private readonly IUiTimer _refreshTimer;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAlertSignal _alert;
    private readonly IUiTimer _retryTimer;

    /// <summary>
    /// Waits between retries after a failed fetch. Three attempts spread over
    /// two minutes: long enough to cover the dead spot behind a hill or a tunnel
    /// the vehicle just drove through, short enough that it does not sit on an
    /// error message until the next scheduled refresh — which at a half-hour
    /// interval is a very long time to look at a stale panel.
    /// </summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromSeconds(60)
    ];

    private int _retryAttempt;

    public MainViewModel(
        ClockViewModel clock,
        DashboardViewModel dashboard,
        MapViewModel map,
        WebRadarViewModel webRadar,
        TrendViewModel trend,
        DiagnosticsViewModel diagnostics,
        SettingsViewModel settings,
        UpdateViewModel update,
        AppSettings appSettings,
        GpsSerialService gps,
        IUiTimerFactory timers,
        IUiDispatcher dispatcher,
        IAlertSignal alert)
    {
        _dispatcher = dispatcher;
        _alert = alert;
        Clock = clock;
        Dashboard = dashboard;
        Map = map;
        WebRadar = webRadar;
        Trend = trend;
        Diagnostics = diagnostics;
        Settings = settings;
        Update = update;
        _settings = appSettings;
        _gps = gps;

        // Keep the "data age" label truthful between refreshes.
        Clock.Tick += OnTick;

        // A new assessment flows straight through to the map and the trend chart.
        Dashboard.AssessmentUpdated += Map.ApplyAssessment;
        Dashboard.AssessmentUpdated += WebRadar.ApplyAssessment;
        Dashboard.AssessmentUpdated += OnAssessmentForTrend;
        Dashboard.WarningsAlerted += OnWarningsAlerted;

        Settings.SettingsApplied += OnSettingsApplied;

        _gps.StatusChanged += OnGpsStatus;
        _gps.FixReceived += OnGpsFix;

        _refreshTimer = timers.Create(
            TimeSpan.FromSeconds(Math.Clamp(appSettings.WeatherRefreshSeconds, 60, 3600)),
            () => _ = RefreshAllAsync());

        // One-shot in effect: it stops itself in the tick and is restarted with a
        // new interval for the attempt after that.
        _retryTimer = timers.Create(RetryDelays[0], OnRetryDue);

        AlwaysOnTop = appSettings.AlwaysOnTop;
    }

    public ClockViewModel Clock { get; }

    public DashboardViewModel Dashboard { get; }

    public MapViewModel Map { get; }

    public WebRadarViewModel WebRadar { get; }

    public TrendViewModel Trend { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public SettingsViewModel Settings { get; }

    public UpdateViewModel Update { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private string _gpsIndicator = "GPS: —";

    // ------------------------------------------------------------- alerting

    /// <summary>Text of the standing alert banner; empty when there is none.</summary>
    [ObservableProperty]
    private string _alertBanner = string.Empty;

    /// <summary>True while an unacknowledged warning alert is up.</summary>
    [ObservableProperty]
    private bool _hasAlert;

    /// <summary>Set when the alert tone could not be played on this machine.</summary>
    [ObservableProperty]
    private bool _alertWasSilent;

    /// <summary>Clears the banner. The warning itself stays in the list.</summary>
    [RelayCommand]
    private void AcknowledgeAlert()
    {
        HasAlert = false;
        AlertBanner = string.Empty;
        AlertWasSilent = false;
    }

    /// <summary>Switches to the tab the warning is written out on, and clears the banner.</summary>
    [RelayCommand]
    private void ShowWarnings()
    {
        SelectedTabIndex = 1;
        AcknowledgeAlert();
    }

    /// <summary>
    /// A warning nobody has seen yet. The banner stays until it is acknowledged
    /// — an alert that fades after a few seconds is one that gets missed by
    /// whoever stepped out for those few seconds.
    ///
    /// The tab is not switched. Someone reading a wind direction off the map
    /// during a briefing should not have the view pulled out from under them;
    /// the banner sits across the top of every tab instead.
    /// </summary>
    private void OnWarningsAlerted(IReadOnlyList<WarningAlert> alerts) => Dispatch(() =>
    {
        if (alerts.Count == 0)
        {
            return;
        }

        AlertBanner = alerts.Count == 1
            ? alerts[0].Describe()
            : $"{alerts[0].Describe()} (und {alerts.Count - 1} weitere)";

        HasAlert = true;
        AlertWasSilent = !_alert.Sound();
    });

    // ------------------------------------------------------------- schedule

    /// <summary>Starts the first refresh and the periodic schedule.</summary>
    public async Task InitialiseAsync()
    {
        // Connect a preconfigured receiver before the first position lookup, so
        // the very first refresh can already use a real fix.
        if (!string.IsNullOrWhiteSpace(_settings.GpsPortName) &&
            _settings.LocationMode != LocationMode.Manual)
        {
            _gps.Open(_settings.GpsPortName, _settings.GpsBaudRate);
        }

        // Before the first request goes out, so a start without network shows
        // the stored picture immediately rather than after the timeout expires.
        Dashboard.ShowStoredStateIfAny();

        await RefreshAllAsync().ConfigureAwait(true);
        _refreshTimer.Start();

        // Last, and only ever a look: the weather matters more than the version,
        // and a failed check must not delay the first reading.
        try
        {
            await Update.CheckSilentlyAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Reported inside the update panel; never worth a dialog at startup.
        }
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await Dashboard.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        await Map.RefreshRadarCommand.ExecuteAsync(null).ConfigureAwait(true);

        ScheduleRetryIfNeeded();
    }

    /// <summary>
    /// Queues another attempt after a failed fetch, or stands down after one that
    /// worked.
    ///
    /// Without this a single dropped request leaves the panel showing an error
    /// until the next scheduled refresh. On a vehicle that is the normal case
    /// rather than the exceptional one — the link comes and goes with the
    /// terrain, and the request that failed thirty seconds ago usually succeeds
    /// now.
    /// </summary>
    private void ScheduleRetryIfNeeded()
    {
        _retryTimer.Stop();

        if (!Dashboard.HasError)
        {
            _retryAttempt = 0;
            Dashboard.RetryNotice = string.Empty;
            return;
        }

        if (_retryAttempt >= RetryDelays.Length)
        {
            Dashboard.RetryNotice =
                $"Nach {RetryDelays.Length} Versuchen kein Abruf möglich — " +
                "nächster Versuch mit dem regulären Intervall.";
            _retryAttempt = 0;
            return;
        }

        TimeSpan delay = RetryDelays[_retryAttempt];
        _retryAttempt++;

        Dashboard.RetryNotice =
            $"Neuer Versuch in {(int)delay.TotalSeconds} s ({_retryAttempt}/{RetryDelays.Length}).";

        _retryTimer.Interval = delay;
        _retryTimer.Start();
    }

    private void OnRetryDue()
    {
        _retryTimer.Stop();
        _ = RefreshAllAsync();
    }

    private void OnTick(DateTimeOffset now) => Dashboard.UpdateAge(now);

    private void OnAssessmentForTrend(Core.Assessment.TacticalAssessment assessment) =>
        Trend.Apply(assessment, DateTimeOffset.Now);

    private void OnSettingsApplied()
    {
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.WeatherRefreshSeconds, 60, 3600));
        AlwaysOnTop = _settings.AlwaysOnTop;
        Dashboard.ApplyAlertSettings();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _retryTimer.Stop();
        Clock.Tick -= OnTick;
        Dashboard.AssessmentUpdated -= Map.ApplyAssessment;
        Dashboard.AssessmentUpdated -= WebRadar.ApplyAssessment;
        Dashboard.AssessmentUpdated -= OnAssessmentForTrend;
        Dashboard.WarningsAlerted -= OnWarningsAlerted;
        Settings.SettingsApplied -= OnSettingsApplied;
        _gps.StatusChanged -= OnGpsStatus;
        _gps.FixReceived -= OnGpsFix;

        _refreshTimer.Dispose();
        _retryTimer.Dispose();
        Clock.Dispose();
        Map.Dispose();
    }

    private void OnGpsStatus(string status) =>
        // Serial events arrive off the UI thread.
        Dispatch(() =>
        {
            Settings.ReportGpsStatus(status);
            GpsIndicator = status.StartsWith("GPS", StringComparison.Ordinal) ? status : $"GPS: {status}";
        });

    private void OnGpsFix(Core.Models.GeoPosition fix) =>
        Dispatch(() => GpsIndicator = $"GPS: Fix {fix.Latitude:F4}/{fix.Longitude:F4} ({fix.SourceLabel})");

    private void Dispatch(Action action) => _dispatcher.Post(action);
}
