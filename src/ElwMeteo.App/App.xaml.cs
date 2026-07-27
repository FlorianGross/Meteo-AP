using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using ElwMeteo.App.Services;
using ElwMeteo.App.ViewModels;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Diagnostics;
using ElwMeteo.Core.Reporting;
using ElwMeteo.Core.Services;

namespace ElwMeteo.App;

/// <summary>
/// Composition root. The object graph is small enough that wiring it by hand is
/// clearer — and easier to reason about at 3 a.m. — than a container.
/// </summary>
public partial class App : Application
{
    private HttpClient? _httpClient;
    private RequestLog? _requestLog;
    private SettingsStore? _settingsStore;
    private DispatcherTimer? _settingsFlushTimer;
    private GpsSerialService? _gps;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dates, decimal commas and weekday names must all come out German
        // regardless of the machine's regional settings.
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(german.IetfLanguageTag)));

        // A failed web request must never take the application down with it.
        DispatcherUnhandledException += OnUnhandledException;

        AppSettings settings = AppSettings.Load();

        // One writer for the whole application. Changes on any tab are marked
        // dirty and flushed on a timer, so a slider drag costs one file write
        // rather than one per pixel — and nothing is lost on the next start.
        _settingsStore = new SettingsStore(settings);

        _requestLog = new RequestLog();
        _httpClient = CreateHttpClient(_requestLog);
        _gps = new GpsSerialService();

        var weather = new OpenMeteoWeatherProvider(_httpClient);
        var brightSky = new BrightSkyProvider(_httpClient);
        // Bright Sky first (the WarnWetter CAP feed), GeoServer as the fallback.
        var warnings = new CompositeWarningProvider(brightSky, new DwdWarningProvider(_httpClient));
        var radar = new RainViewerProvider(_httpClient);
        var windField = new WindFieldProvider(_httpClient);
        var capabilities = new WmsCapabilitiesService(_httpClient);
        var connectivity = new ConnectivityCheck(_httpClient);
        var geocoding = new GeocodingService(_httpClient);
        var ipLocation = new IpLocationProvider(_httpClient);
        var csvLogger = new SnapshotCsvLogger(settings.ResolveCsvDirectory());
        var locationResolver = new LocationResolver(settings, _gps, ipLocation);

        _mainViewModel = new MainViewModel(
            new ClockViewModel(),
            new DashboardViewModel(weather, warnings, geocoding, locationResolver, csvLogger, brightSky, settings),
            new MapViewModel(radar, capabilities, windField, weather, _settingsStore),
            new WebRadarViewModel(_settingsStore),
            new TrendViewModel(),
            new DiagnosticsViewModel(_requestLog, connectivity, capabilities),
            new SettingsViewModel(_settingsStore, _gps, geocoding),
            settings,
            _gps);

        // Map tile failures happen inside the page; route them into the same log.
        Views.MapView.SharedLog = _requestLog;

        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;
        window.Show();

        // Pending settings are written a couple of seconds after the last
        // change — long enough that a slider drag is a single write, short
        // enough that a power cut mid-shift does not cost the setup.
        _settingsFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _settingsFlushTimer.Tick += (_, _) => _settingsStore.Flush();
        _settingsFlushTimer.Start();

        // Kick off the first fetch after the window is up, so the UI paints first.
        _ = Dispatcher.InvokeAsync(async () => await _mainViewModel.InitialiseAsync());
    }

    private static HttpClient CreateHttpClient(RequestLog log)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        // Every call is recorded, so a failure can be diagnosed from the URL and
        // status rather than from an empty panel.
        var client = new HttpClient(new RequestLoggingHandler(log) { InnerHandler = handler })
        {
            // Long enough for a slow cellular link, short enough that a dead
            // network does not leave the refresh button stuck.
            Timeout = TimeSpan.FromSeconds(20)
        };

        // Nominatim's usage policy requires an identifying User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ELW-Meteo", "1.0"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9");

        return client;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unerwarteter Fehler:\n\n{e.Exception.Message}\n\n" +
            "Die Anwendung läuft weiter. Bitte Daten erneut abrufen.",
            "ELW-Meteo",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Anything changed in the last two seconds would otherwise be lost.
        _settingsFlushTimer?.Stop();
        _settingsStore?.Flush();

        _mainViewModel?.Dispose();
        _gps?.Dispose();
        _httpClient?.Dispose();

        base.OnExit(e);
    }
}
