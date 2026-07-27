using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ElwMeteo.App.ViewModels;
using ElwMeteo.Core.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace ElwMeteo.App.Views;

/// <summary>
/// Hosts the Leaflet map in WebView2 and shuttles state between the view model
/// and the page.
/// </summary>
public partial class MapView : UserControl
{
    private MapViewModel? _viewModel;
    private bool _isWebViewReady;

    /// <summary>
    /// Tile failures inside the page never reach HttpClient, so they are pushed
    /// into the shared log here — otherwise the diagnostics tab would show a
    /// clean sheet while the map is visibly broken.
    /// </summary>
    public static RequestLog? SharedLog { get; set; }

    /// <summary>State pushed before the page was ready, replayed once it is.</summary>
    private string? _pendingState;

    public MapView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MapViewModel oldViewModel)
        {
            oldViewModel.StateChanged -= OnStateChanged;
        }

        _viewModel = e.NewValue as MapViewModel;

        if (_viewModel is not null)
        {
            _viewModel.StateChanged += OnStateChanged;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // A TabControl unloads and reloads its content on every tab switch, so
        // Loaded fires repeatedly — but the WebView is only set up once.
        if (_isWebViewReady)
        {
            return;
        }

        // Tear down with the window, never on Unloaded: leaving the tab would
        // otherwise dispose the browser and leave a dead map behind.
        if (Window.GetWindow(this) is { } window)
        {
            window.Closed += OnWindowClosed;
        }

        await InitialiseWebViewAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.StateChanged -= OnStateChanged;
        }

        MapWebView.Dispose();
    }

    private async Task InitialiseWebViewAsync()
    {
        try
        {
            // Keep the browser profile beside our own settings rather than in the
            // install directory, which is usually read-only under Program Files.
            string userDataFolder = Path.Combine(
                Core.Configuration.AppSettings.DefaultDirectory, "WebView2");
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment environment = await CoreWebView2Environment
                .CreateAsync(userDataFolder: userDataFolder);

            await MapWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = MapWebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            core.WebMessageReceived += OnWebMessageReceived;

            string mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "map.html");
            if (!File.Exists(mapPath))
            {
                ShowOverlay("Kartendatei fehlt",
                    $"Die Datei Assets/map.html wurde nicht gefunden:\n{mapPath}");
                return;
            }

            core.Navigate(new Uri(mapPath).AbsoluteUri);
            _isWebViewReady = true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowOverlay(
                "WebView2-Runtime nicht installiert",
                "Die Kartenansicht benötigt die Microsoft Edge WebView2-Runtime. " +
                "Auf aktuellen Windows-Installationen ist sie bereits vorhanden; " +
                "andernfalls kann sie kostenlos von Microsoft nachinstalliert werden " +
                "(\"Evergreen Standalone Installer\"). Die Registerkarte „Lage & Wetter“ " +
                "funktioniert unabhängig davon.");
        }
        catch (Exception ex)
        {
            ShowOverlay("Karte konnte nicht geladen werden", ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "ready":
                    HideOverlay();
                    _viewModel.NotifyPageReady();

                    if (_pendingState is not null)
                    {
                        Apply(_pendingState);
                        _pendingState = null;
                    }

                    break;

                case "layerError":
                    if (root.TryGetProperty("title", out JsonElement failed))
                    {
                        SharedLog?.AddExternalFailure(
                            failed.GetString() ?? "Kartenlayer",
                            "Kacheln konnten nicht geladen werden.");
                    }

                    break;

                case "mapClick":
                    if (root.TryGetProperty("lat", out JsonElement lat) &&
                        root.TryGetProperty("lon", out JsonElement lon))
                    {
                        _viewModel.ReportMapClick(lat.GetDouble(), lon.GetDouble());
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // A message we do not understand is not worth interrupting the user over.
        }
    }

    private void OnStateChanged(string json)
    {
        if (!_isWebViewReady || MapWebView.CoreWebView2 is null)
        {
            _pendingState = json;
            return;
        }

        Apply(json);
    }

    private void Apply(string json)
    {
        try
        {
            // Pass the state as a single JSON string argument.
            string script = $"window.elwMeteo && window.elwMeteo.apply({JsonSerializer.Serialize(json)});";
            _ = MapWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // The page may be mid-navigation; the next push will land.
        }
    }

    private void ShowOverlay(string title, string text)
    {
        MapOverlayTitle.Text = title;
        MapOverlayText.Text = text;
        MapOverlay.Visibility = Visibility.Visible;
    }

    private void HideOverlay() => MapOverlay.Visibility = Visibility.Collapsed;
}
