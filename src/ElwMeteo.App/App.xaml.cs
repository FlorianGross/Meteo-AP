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

        _httpClient = CreateHttpClient();
        _gps = new GpsSerialService();

        var weather = new OpenMeteoWeatherProvider(_httpClient);
        var warnings = new DwdWarningProvider(_httpClient);
        var radar = new RainViewerProvider(_httpClient);
        var windField = new WindFieldProvider(_httpClient);
        var geocoding = new GeocodingService(_httpClient);
        var ipLocation = new IpLocationProvider(_httpClient);
        var csvLogger = new SnapshotCsvLogger(settings.ResolveCsvDirectory());
        var locationResolver = new LocationResolver(settings, _gps, ipLocation);

        _mainViewModel = new MainViewModel(
            new ClockViewModel(),
            new DashboardViewModel(weather, warnings, geocoding, locationResolver, csvLogger, settings),
            new MapViewModel(radar, windField, weather, settings),
            new TrendViewModel(),
            new SettingsViewModel(settings, _gps, geocoding),
            settings,
            _gps);

        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;
        window.Show();

        // Kick off the first fetch after the window is up, so the UI paints first.
        _ = Dispatcher.InvokeAsync(async () => await _mainViewModel.InitialiseAsync());
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler)
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
        _mainViewModel?.Dispose();
        _gps?.Dispose();
        _httpClient?.Dispose();

        base.OnExit(e);
    }
}
