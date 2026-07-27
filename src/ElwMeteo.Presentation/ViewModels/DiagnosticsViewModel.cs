using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Core.Diagnostics;
using ElwMeteo.Core.Maps;
using ElwMeteo.Core.Services;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>
/// Tab 6 — what the application asked for, what came back, and which services
/// answer at all.
///
/// This exists because every remote failure in this application looks the same
/// from the outside: an empty panel. The log turns "funktioniert nicht" into a
/// URL and a status code.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly RequestLog _log;
    private readonly ConnectivityCheck _connectivity;
    private readonly WmsCapabilitiesService _capabilities;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;

    public DiagnosticsViewModel(
        RequestLog log,
        ConnectivityCheck connectivity,
        WmsCapabilitiesService capabilities,
        IUiDispatcher dispatcher,
        IClipboardService clipboard)
    {
        _log = log;
        _connectivity = connectivity;
        _capabilities = capabilities;
        _dispatcher = dispatcher;
        _clipboard = clipboard;

        // The handler records from background threads; marshal to the UI.
        _log.Recorded += _ => _dispatcher.Post(RefreshFromLog);

        RefreshFromLog();
    }

    public ObservableCollection<RequestRecord> Requests { get; } = [];

    public ObservableCollection<EndpointStatus> Endpoints { get; } = [];

    public ObservableCollection<WmsLayerInfo> DwdLayers { get; } = [];

    [ObservableProperty]
    private string _summary = "Noch keine Anfragen aufgezeichnet.";

    [ObservableProperty]
    private string _connectivitySummary = "Verbindungstest noch nicht gelaufen.";

    [ObservableProperty]
    private string _dwdLayerSummary = "DWD-Layerliste noch nicht abgerufen.";

    [ObservableProperty]
    private string _dwdLayerFilter = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private IReadOnlyList<WmsLayerInfo> _allDwdLayers = [];

    // ------------------------------------------------------------- commands

    /// <summary>Probes every service the application depends on.</summary>
    [RelayCommand]
    private async Task RunConnectivityCheckAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            Endpoints.Clear();
            ConnectivitySummary = "Verbindungstest läuft …";

            IReadOnlyList<EndpointStatus> results = await _connectivity
                .RunAsync(cancellationToken)
                .ConfigureAwait(true);

            foreach (EndpointStatus status in results)
            {
                Endpoints.Add(status);
            }

            ConnectivitySummary = ConnectivityCheck.Summarise(results);
        }
        catch (OperationCanceledException)
        {
            ConnectivitySummary = "Verbindungstest abgebrochen.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads the DWD layer list. This is the answer to "which layer is it
    /// really called" — the question no amount of guessing settles.
    /// </summary>
    [RelayCommand]
    private async Task LoadDwdLayersAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            DwdLayerSummary = "Layerliste wird abgerufen …";

            _allDwdLayers = await _capabilities
                .GetLayersAsync(MapLayerCatalog.DwdWmsEndpoint, cancellationToken)
                .ConfigureAwait(true);

            ApplyDwdFilter();

            int animatable = _allDwdLayers.Count(l => l.IsAnimatable);
            DwdLayerSummary = $"{_allDwdLayers.Count} Layer verfügbar, davon {animatable} mit Zeitachse.";
        }
        catch (CapabilitiesException ex)
        {
            _allDwdLayers = [];
            DwdLayers.Clear();
            DwdLayerSummary = ex.Message;
        }
        catch (OperationCanceledException)
        {
            DwdLayerSummary = "Abruf abgebrochen.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyLog()
    {
        string text = BuildReport();

        StatusMessage = _clipboard.TrySetText(text)
            ? "Protokoll in die Zwischenablage kopiert."
            : "Zwischenablage ist belegt — bitte erneut versuchen.";
    }

    [RelayCommand]
    private void ClearLog()
    {
        _log.Clear();
        RefreshFromLog();
        StatusMessage = "Protokoll geleert.";
    }

    partial void OnDwdLayerFilterChanged(string value) => ApplyDwdFilter();

    // -------------------------------------------------------------- helpers

    /// <summary>The full report: connectivity, request log and resolved layers.</summary>
    private string BuildReport()
    {
        var text = new System.Text.StringBuilder();
        DateTimeOffset now = DateTimeOffset.Now;

        text.AppendLine(_log.ToPlainText(now));

        if (Endpoints.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("VERBINDUNGSTEST");
            text.AppendLine(new string('=', 70));
            text.AppendLine(ConnectivitySummary);
            text.AppendLine();

            foreach (EndpointStatus status in Endpoints)
            {
                text.AppendLine($"  [{(status.Reachable ? "ok" : "  ")}] {status.Name}");
                text.AppendLine($"        {status.Label}");
                text.AppendLine($"        {status.Url}");
            }
        }

        if (_allDwdLayers.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("DWD-LAYER LAUT SERVER");
            text.AppendLine(new string('=', 70));

            foreach (WmsLayerInfo layer in _allDwdLayers)
            {
                string time = layer.IsAnimatable
                    ? $"  [Zeitachse: {layer.Time.Instants.Count} Schritte]"
                    : string.Empty;

                text.AppendLine($"  {layer.Name}{time}");
            }
        }

        return text.ToString();
    }

    private void RefreshFromLog()
    {
        Requests.Clear();
        foreach (RequestRecord record in _log.Snapshot())
        {
            Requests.Add(record);
        }

        var summary = _log.HostSummary();
        int failures = summary.Sum(entry => entry.Failures);
        int total = summary.Sum(entry => entry.Total);

        Summary = total == 0
            ? "Noch keine Anfragen aufgezeichnet."
            : failures == 0
                ? $"{total} Anfragen, alle erfolgreich."
                : $"{total} Anfragen, davon {failures} fehlgeschlagen — betroffene Dienste: " +
                  string.Join(", ", summary.Where(e => e.Failures > 0).Select(e => e.Host));
    }

    private void ApplyDwdFilter()
    {
        string needle = DwdLayerFilter.Trim();

        DwdLayers.Clear();
        foreach (WmsLayerInfo layer in _allDwdLayers)
        {
            bool matches = needle.Length == 0 ||
                layer.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                layer.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

            if (matches)
            {
                DwdLayers.Add(layer);
            }
        }
    }

}
