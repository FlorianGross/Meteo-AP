using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Kiosk;
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
    private readonly IScreenService _screens;
    private readonly TabCarousel _carousel = new();
    private readonly IUiTimer _carouselTimer;

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
        IAlertSignal alert,
        IScreenService? screens = null)
    {
        _dispatcher = dispatcher;
        _screens = screens ?? new NoScreenService();
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

        _carouselTimer = timers.Create(
            TabCarousel.ClampInterval(appSettings.CarouselIntervalSeconds),
            OnCarouselDue);

        ApplyCarouselSettings();
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

    // --------------------------------------------------- vehicle / kiosk mode

    /// <summary>True while the unattended rotation is switching tabs by itself.</summary>
    [ObservableProperty]
    private bool _carouselEnabled;

    /// <summary>What the rotation is doing, for the status line.</summary>
    [ObservableProperty]
    private string _carouselStatus = string.Empty;

    /// <summary>Mirrors the window state, so the button can say which way it goes.</summary>
    [ObservableProperty]
    private bool _isFullScreen;

    /// <summary>Set when a configured display was not found at startup.</summary>
    [ObservableProperty]
    private string _screenNotice = string.Empty;

    /// <summary>
    /// Switches the rotation on or off by hand. Bound to a toolbar button as
    /// well as to F9: a kiosk nobody can stop is a kiosk somebody unplugs.
    /// </summary>
    [RelayCommand]
    private void ToggleCarousel()
    {
        _settings.CarouselEnabled = !_settings.CarouselEnabled;
        ApplyCarouselSettings();

        // Deliberately persisted. Somebody who stops the rotation to work with
        // the map wants it stopped after the next restart as well — on a vehicle
        // that restart may be the moment they arrive at the next incident.
        TrySaveSettings();
    }

    /// <summary>
    /// Full screen on or off. The escape hatch for the whole kiosk idea: bound
    /// to F11 and to Escape in both heads, so a screen that came up without
    /// window decoration can always be got back under control without knowing
    /// where the settings file lives.
    /// </summary>
    [RelayCommand]
    private void ToggleFullScreen()
    {
        NoteInteraction();

        if (_screens.TrySetFullScreen(!IsFullScreen, out string? error))
        {
            IsFullScreen = _screens.IsFullScreen;
            return;
        }

        ScreenNotice = error ?? "Vollbild konnte nicht umgeschaltet werden.";
    }

    /// <summary>
    /// Leaves full screen and does nothing when it is already off — what Escape
    /// is wired to. Separate from the toggle on purpose: Escape must never be
    /// the key that *enters* full screen.
    /// </summary>
    [RelayCommand]
    private void LeaveFullScreen()
    {
        if (!IsFullScreen)
        {
            return;
        }

        ToggleFullScreen();
    }

    /// <summary>
    /// Records that somebody is operating the application, which holds the
    /// rotation for the configured quiet stretch. Called from both heads on key
    /// and pointer input.
    /// </summary>
    public void NoteInteraction()
    {
        // The suppression of the rotation's own tab change lives in the carousel,
        // where it can be tested without a window.
        _carousel.NoteInteraction(DateTimeOffset.Now);

        if (CarouselEnabled)
        {
            UpdateCarouselStatus();
        }
    }

    /// <summary>
    /// Applies the screen mapping and the full-screen preference. Called once the
    /// window exists, which is why it is not in the constructor.
    /// </summary>
    public void ApplyWindowPlacement()
    {
        IReadOnlyList<ScreenInfo> available = _screens.List();

        if (ScreenChoice.IsPreferenceMissing(available, _settings.PreferredScreenId))
        {
            // Said out loud rather than silently corrected: an operator who
            // configured the second monitor should learn that it is not plugged
            // in, not wonder why the window keeps opening on the laptop panel.
            ScreenNotice =
                $"Bildschirm „{_settings.PreferredScreenId}“ ist nicht angeschlossen — " +
                "es wird der Hauptbildschirm verwendet.";
        }

        ScreenInfo? target = ScreenChoice.Select(available, _settings.PreferredScreenId);

        if (target is not null &&
            !_screens.TryApply(target, _settings.StartFullScreen, out string? error))
        {
            ScreenNotice = error ?? "Bildschirmzuordnung fehlgeschlagen.";
        }
        else if (target is null && _settings.StartFullScreen)
        {
            _screens.TrySetFullScreen(true, out _);
        }

        IsFullScreen = _screens.IsFullScreen;
    }

    /// <summary>Takes the rotation settings over and starts or stops the timer.</summary>
    private void ApplyCarouselSettings()
    {
        _carousel.Configure(_settings.ResolveCarouselStations());
        _carousel.SyncToTab(SelectedTabIndex);

        _carouselTimer.Interval = TabCarousel.ClampInterval(_settings.CarouselIntervalSeconds);
        CarouselEnabled = _settings.CarouselEnabled && _carousel.CanRotate;

        if (CarouselEnabled)
        {
            _carouselTimer.Start();
        }
        else
        {
            _carouselTimer.Stop();
        }

        UpdateCarouselStatus();
    }

    /// <summary>
    /// One rotation step is due. Whether it is actually taken is
    /// <see cref="TabCarousel.ShouldAdvance"/>'s call — the timer keeps running
    /// through a pause so the rotation resumes by itself once the operator stops
    /// touching anything.
    /// </summary>
    private void OnCarouselDue()
    {
        if (!CarouselEnabled)
        {
            return;
        }

        KioskStation? next = _carousel.Advance(DateTimeOffset.Now, IdleGrace);

        if (next is null)
        {
            // Held by an interaction. The timer keeps running, so the rotation
            // resumes by itself once nobody is touching anything.
            UpdateCarouselStatus();
            return;
        }

        try
        {
            // Raises the same notification a person's click does; the carousel
            // ignores it until the step is complete.
            SelectedTabIndex = next.TabIndex;
        }
        finally
        {
            _carousel.CompleteAdvance();
        }

        UpdateCarouselStatus();
    }

    private TimeSpan IdleGrace =>
        TabCarousel.ClampIdleGrace(_settings.CarouselIdleGraceSeconds);

    private void UpdateCarouselStatus()
    {
        if (!CarouselEnabled)
        {
            CarouselStatus = string.Empty;
            return;
        }

        bool holding = _carousel.IsHeld(DateTimeOffset.Now, IdleGrace);

        string current = _carousel.Current?.Title ?? "—";

        CarouselStatus = holding
            ? $"Rundlauf angehalten (Bedienung) — {current}"
            : $"Rundlauf: {current} · {(int)_carouselTimer.Interval.TotalSeconds} s";
    }

    /// <summary>
    /// A tab change is an interaction when a person made it, and the point the
    /// rotation continues from either way.
    /// </summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!_carousel.IsAdvancing)
        {
            NoteInteraction();
            _carousel.SyncToTab(value);
        }

        UpdateCarouselStatus();
    }

    /// <summary>
    /// Writes the settings file, swallowing the failure. The rotation state is
    /// a convenience; a read-only profile must not turn pressing a toolbar
    /// button into a crash.
    /// </summary>
    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing the operator can do about it here; the rotation is on
            // either way for this session.
        }
    }

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
        ApplyCarouselSettings();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _retryTimer.Stop();
        _carouselTimer.Stop();
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
        _carouselTimer.Dispose();
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
