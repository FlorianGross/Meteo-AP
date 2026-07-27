using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Diagnostics;
using ElwMeteo.Core.Reporting;
using ElwMeteo.Core.Services;
using ElwMeteo.Desktop.Platform;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Presentation.Services;
using ElwMeteo.Presentation.ViewModels;

namespace ElwMeteo.Desktop;

/// <summary>
/// Composition root of the cross-platform head.
///
/// Deliberately the same object graph as the Windows head, wired by hand for the
/// same reason: at three in the morning a reader should be able to follow which
/// object got which dependency without learning a container first. Everything
/// below the view models is shared code — only the four platform services and
/// the views differ.
/// </summary>
public partial class App : global::Avalonia.Application
{
    private HttpClient? _httpClient;
    private RequestLog? _requestLog;
    private SettingsStore? _settingsStore;
    private IUiTimer? _settingsFlushTimer;
    private GpsSerialService? _gps;
    private MainViewModel? _mainViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppSettings settings = AppSettings.Load();
        _settingsStore = new SettingsStore(settings);

        _requestLog = new RequestLog();
        _httpClient = CreateHttpClient(_requestLog);
        _gps = new GpsSerialService();

        var weather = new OpenMeteoWeatherProvider(_httpClient);
        var brightSky = new BrightSkyProvider(_httpClient);
        var warnings = new CompositeWarningProvider(brightSky, new DwdWarningProvider(_httpClient));
        var radar = new RainViewerProvider(_httpClient);
        var windField = new WindFieldProvider(_httpClient);
        var capabilities = new WmsCapabilitiesService(_httpClient);
        var connectivity = new ConnectivityCheck(_httpClient);
        var geocoding = new GeocodingService(_httpClient);
        var ipLocation = new IpLocationProvider(_httpClient);
        var csvLogger = new SnapshotCsvLogger(settings.ResolveCsvDirectory());
        var locationResolver = new LocationResolver(settings, _gps, ipLocation);

        var dispatcher = new AvaloniaUiDispatcher();
        var timers = new AvaloniaTimerFactory();
        var clipboard = new AvaloniaClipboard();
        var shell = new SystemShellLauncher();

        _mainViewModel = new MainViewModel(
            new ClockViewModel(timers),
            new DashboardViewModel(weather, warnings, geocoding, locationResolver, csvLogger, brightSky, settings, clipboard),
            new MapViewModel(radar, capabilities, windField, weather, _settingsStore, timers),
            new WebRadarViewModel(_settingsStore, shell),
            new TrendViewModel(),
            new DiagnosticsViewModel(_requestLog, connectivity, capabilities, dispatcher, clipboard),
            new SettingsViewModel(_settingsStore, _gps, geocoding, shell),
            settings,
            _gps,
            timers,
            dispatcher);

        desktop.MainWindow = new MainWindow { DataContext = _mainViewModel };
        desktop.ShutdownRequested += (_, _) => Shutdown();

        _settingsFlushTimer = timers.Create(TimeSpan.FromSeconds(2), () => _settingsStore.Flush());
        _settingsFlushTimer.Start();

        base.OnFrameworkInitializationCompleted();

        // First fetch after the window is up, so the interface paints first.
        dispatcher.Post(() => _ = _mainViewModel.InitialiseAsync());
    }

    private void Shutdown()
    {
        _settingsFlushTimer?.Stop();
        _settingsStore?.Flush();

        _mainViewModel?.Dispose();
        _gps?.Dispose();
        _httpClient?.Dispose();
    }

    private static HttpClient CreateHttpClient(RequestLog log)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var client = new HttpClient(new RequestLoggingHandler(log) { InnerHandler = handler })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        // Nominatim's usage policy requires an identifying User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ELW-Meteo", "1.0"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9");

        return client;
    }
}
