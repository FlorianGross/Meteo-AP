using System.IO;
using System.Windows;
using System.Windows.Controls;
using ElwMeteo.Presentation.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace ElwMeteo.App.Views;

/// <summary>
/// Hosts the providers' own web pages in WebView2.
///
/// Unlike the Leaflet map this browses the open internet, so it is locked down a
/// little further: only http and https are followed, pop-ups stay in the same
/// view instead of spawning windows, and downloads are refused. What the
/// operator sees is a page, not a file dialog on a vehicle screen.
/// </summary>
public partial class WebRadarView : UserControl
{
    private WebRadarViewModel? _viewModel;
    private bool _isWebViewReady;

    /// <summary>URL requested before the browser was ready, replayed once it is.</summary>
    private string? _pendingUrl;

    public WebRadarView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WebRadarViewModel oldViewModel)
        {
            oldViewModel.NavigationRequested -= OnNavigationRequested;
        }

        _viewModel = e.NewValue as WebRadarViewModel;

        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;

            // The view model picks its viewer in its constructor, so that first
            // request is raised before anyone is subscribed. If the browser was
            // already up when the data context arrived, nothing would ever ask
            // it to navigate and the panel would stay blank.
            if (_isWebViewReady)
            {
                _viewModel.NotifyViewReady();
            }
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // A TabControl unloads and reloads its content on every tab switch, so
        // Loaded fires repeatedly — the browser is only set up once.
        if (_isWebViewReady)
        {
            return;
        }

        // Tear down with the window, never on Unloaded: leaving the tab would
        // otherwise dispose the browser and come back to a blank panel.
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
            _viewModel.NavigationRequested -= OnNavigationRequested;
        }

        RadarWebView.Dispose();
    }

    private async Task InitialiseWebViewAsync()
    {
        try
        {
            // Same profile folder as the map view, so cookies and consent dialogs
            // are answered once rather than on every start.
            string userDataFolder = Path.Combine(
                Core.Configuration.AppSettings.DefaultDirectory, "WebView2");
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment environment = await CoreWebView2Environment
                .CreateAsync(userDataFolder: userDataFolder);

            await RadarWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = RadarWebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            // These are real websites, so the usual browser affordances stay on:
            // right-click, zoom and text selection are what makes them usable.
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsZoomControlEnabled = true;

            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.DownloadStarting += OnDownloadStarting;

            _isWebViewReady = true;
            HideOverlay();

            if (_pendingUrl is not null)
            {
                Navigate(_pendingUrl);
                _pendingUrl = null;
            }
            else
            {
                _viewModel?.NotifyViewReady();
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowOverlay(
                "WebView2-Runtime nicht installiert",
                "Diese Ansicht benötigt die Microsoft Edge WebView2-Runtime. " +
                "Auf aktuellen Windows-Installationen ist sie bereits vorhanden; " +
                "andernfalls kann sie kostenlos von Microsoft nachinstalliert werden " +
                "(\"Evergreen Standalone Installer\"). Mit „Im Browser öffnen“ lässt sich " +
                "die gewählte Seite auch ohne sie aufrufen.");
        }
        catch (Exception ex)
        {
            ShowOverlay("Ansicht konnte nicht geladen werden", ex.Message);
        }
    }

    private void OnNavigationRequested(string url)
    {
        if (!_isWebViewReady || RadarWebView.CoreWebView2 is null)
        {
            _pendingUrl = url;
            return;
        }

        Navigate(url);
    }

    private void Navigate(string url)
    {
        try
        {
            RadarWebView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            _viewModel?.ReportNavigationCompleted(false, ex.Message);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // A page can try to send the view anywhere — including at a local file.
        // Only the two web schemes are followed.
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            e.Cancel = true;
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        string? detail = e.IsSuccess ? null : DescribeError(e.WebErrorStatus);

        _viewModel?.ReportNavigationCompleted(e.IsSuccess, detail);

        // The browser does its own fetching, so a failure here never reaches
        // HttpClient. Without this the diagnostics tab would show a clean sheet
        // while this panel is visibly broken — the same reason the map view
        // reports its tile errors.
        if (!e.IsSuccess && detail is not null)
        {
            MapView.SharedLog?.AddExternalFailure(
                _viewModel?.CurrentUrl ?? "Web-Radar",
                detail);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Links marked target="_blank" would open a bare browser window with no
        // controls; keep them inside the tab instead.
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Navigate(e.Uri);
        }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Nothing on these pages needs downloading, and a save dialog on a
        // vehicle screen is only ever in the way.
        e.Cancel = true;
    }

    private static string DescribeError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "Adresse nicht auflösbar (DNS)",
        CoreWebView2WebErrorStatus.ConnectionAborted => "Verbindung abgebrochen",
        CoreWebView2WebErrorStatus.ConnectionReset => "Verbindung zurückgesetzt",
        CoreWebView2WebErrorStatus.Disconnected => "keine Netzwerkverbindung",
        CoreWebView2WebErrorStatus.CannotConnect => "Server nicht erreichbar",
        CoreWebView2WebErrorStatus.Timeout => "Zeitüberschreitung",
        CoreWebView2WebErrorStatus.ServerUnreachable => "Server nicht erreichbar",
        CoreWebView2WebErrorStatus.OperationCanceled => "abgebrochen",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
        CoreWebView2WebErrorStatus.CertificateExpired or
        CoreWebView2WebErrorStatus.CertificateRevoked or
        CoreWebView2WebErrorStatus.CertificateIsInvalid => "Zertifikat nicht gültig",
        _ => status.ToString()
    };

    private void ShowOverlay(string title, string text)
    {
        WebOverlayTitle.Text = title;
        WebOverlayText.Text = text;
        WebOverlay.Visibility = Visibility.Visible;
    }

    private void HideOverlay() => WebOverlay.Visibility = Visibility.Collapsed;
}
