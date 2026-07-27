using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.App.Services;
using ElwMeteo.Core.Configuration;

namespace ElwMeteo.App.ViewModels;

/// <summary>
/// Application shell: owns the three tabs, the shared clock and the auto-refresh
/// schedule.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly GpsSerialService _gps;
    private readonly DispatcherTimer _refreshTimer;

    public MainViewModel(
        ClockViewModel clock,
        DashboardViewModel dashboard,
        MapViewModel map,
        TrendViewModel trend,
        DiagnosticsViewModel diagnostics,
        SettingsViewModel settings,
        AppSettings appSettings,
        GpsSerialService gps)
    {
        Clock = clock;
        Dashboard = dashboard;
        Map = map;
        Trend = trend;
        Diagnostics = diagnostics;
        Settings = settings;
        _settings = appSettings;
        _gps = gps;

        // Keep the "data age" label truthful between refreshes.
        Clock.Tick += OnTick;

        // A new assessment flows straight through to the map and the trend chart.
        Dashboard.AssessmentUpdated += Map.ApplyAssessment;
        Dashboard.AssessmentUpdated += OnAssessmentForTrend;

        Settings.SettingsApplied += OnSettingsApplied;

        _gps.StatusChanged += OnGpsStatus;
        _gps.FixReceived += OnGpsFix;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(appSettings.WeatherRefreshSeconds, 60, 3600))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync().ConfigureAwait(true);

        AlwaysOnTop = appSettings.AlwaysOnTop;
    }

    public ClockViewModel Clock { get; }

    public DashboardViewModel Dashboard { get; }

    public MapViewModel Map { get; }

    public TrendViewModel Trend { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private string _gpsIndicator = "GPS: —";

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

        await RefreshAllAsync().ConfigureAwait(true);
        _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await Dashboard.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        await Map.RefreshRadarCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private void OnTick(DateTimeOffset now) => Dashboard.UpdateAge(now);

    private void OnAssessmentForTrend(Core.Assessment.TacticalAssessment assessment) =>
        Trend.Apply(assessment, DateTimeOffset.Now);

    private void OnSettingsApplied()
    {
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.WeatherRefreshSeconds, 60, 3600));
        AlwaysOnTop = _settings.AlwaysOnTop;
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

    private static void Dispatch(Action action)
    {
        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        Clock.Tick -= OnTick;
        Dashboard.AssessmentUpdated -= Map.ApplyAssessment;
        Dashboard.AssessmentUpdated -= OnAssessmentForTrend;
        Settings.SettingsApplied -= OnSettingsApplied;
        _gps.StatusChanged -= OnGpsStatus;
        _gps.FixReceived -= OnGpsFix;

        Clock.Dispose();
        Map.Dispose();
    }
}
