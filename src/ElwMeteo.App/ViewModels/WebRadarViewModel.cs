using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Maps;

namespace ElwMeteo.App.ViewModels;

/// <summary>
/// One built-in viewer with the operator's show/hide decision attached.
/// </summary>
public sealed partial class WebSourceToggle : ObservableObject
{
    private readonly Action _changed;

    public WebSourceToggle(WebViewSource source, bool isVisible, Action changed)
    {
        Source = source;
        _isVisible = isVisible;
        _changed = changed;
    }

    public WebViewSource Source { get; }

    public string Title => Source.Title;

    public string Group => Source.Group;

    public string? Description => Source.Description;

    [ObservableProperty]
    private bool _isVisible;

    partial void OnIsVisibleChanged(bool value) => _changed();
}

/// <summary>
/// Tab — the providers' own web viewers, embedded.
///
/// Every API in this application can be down, renamed or subtly wrong, and each
/// failure looks like an empty panel. A provider's own page is the one thing that
/// keeps working when its API does not, so having them one click away turns a
/// dead end into a second opinion.
///
/// The list is not fixed: built-in entries can be hidden and the operator can
/// store their own pages, so a service can be swapped without a new build.
/// </summary>
public sealed partial class WebRadarViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private double _latitude;
    private double _longitude;

    /// <summary>Set while the list is rebuilt, so toggles do not write back mid-flight.</summary>
    private bool _isReloading;

    public WebRadarViewModel(SettingsStore store)
    {
        _store = store;
        AppSettings settings = store.Settings;
        _settings = settings;
        _latitude = settings.HomeLatitude;
        _longitude = settings.HomeLongitude;
        _zoom = Math.Clamp(settings.WebSourceZoom, 4, 15);

        ReloadSources();

        SelectedSource = Sources.FirstOrDefault(s => s.Id == settings.SelectedWebSourceId)
                         ?? Sources.FirstOrDefault();
    }

    /// <summary>Raised with the URL the browser view should navigate to.</summary>
    public event Action<string>? NavigationRequested;

    /// <summary>Viewers offered in the picker — visible built-ins plus own entries.</summary>
    public ObservableCollection<WebViewSource> Sources { get; } = [];

    /// <summary>Every built-in with its show/hide box, for the management panel.</summary>
    public ObservableCollection<WebSourceToggle> ManagedSources { get; } = [];

    /// <summary>The operator's own entries, so they can be removed again.</summary>
    public ObservableCollection<CustomWebSource> CustomSources { get; } = [];

    [ObservableProperty]
    private WebViewSource? _selectedSource;

    [ObservableProperty]
    private string _currentUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Zoom handed to viewers whose URL carries a {zoom} placeholder.</summary>
    [ObservableProperty]
    private int _zoom = 9;

    [ObservableProperty]
    private string _newSourceName = string.Empty;

    [ObservableProperty]
    private string _newSourceUrl = string.Empty;

    [ObservableProperty]
    private string _editorMessage = string.Empty;

    partial void OnSelectedSourceChanged(WebViewSource? value)
    {
        if (value is null || _isReloading)
        {
            return;
        }

        _settings.SelectedWebSourceId = value.Id;
        _store.RequestSave();
        Navigate();
    }

    partial void OnZoomChanged(int value)
    {
        _settings.WebSourceZoom = value;
        _store.RequestSave();

        // Only worth re-navigating for a viewer that reads the zoom.
        if (SelectedSource?.UrlTemplate.Contains("{zoom}", StringComparison.Ordinal) == true)
        {
            Navigate();
        }
    }

    // ------------------------------------------------------------- commands

    [RelayCommand]
    private void Reload() => Navigate();

    /// <summary>Opens the current page in the operator's normal browser.</summary>
    [RelayCommand]
    private void OpenExternally()
    {
        if (string.IsNullOrWhiteSpace(CurrentUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(CurrentUrl) { UseShellExecute = true });
            StatusMessage = "Im Standardbrowser geöffnet.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Browser konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddCustomSource()
    {
        string url = NewSourceUrl.Trim();
        string name = NewSourceName.Trim();

        if (!WebViewSource.IsAcceptableUrl(url))
        {
            EditorMessage = "Bitte eine vollständige Adresse mit http:// oder https:// angeben.";
            return;
        }

        if (name.Length == 0)
        {
            // A nameless entry would show up as a bare URL in the picker; derive
            // something readable from the host instead.
            name = Uri.TryCreate(url.Replace("{lat}", "0", StringComparison.Ordinal), UriKind.Absolute, out Uri? parsed)
                ? parsed.Host
                : url;
        }

        if (_settings.CustomWebSources.Any(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            EditorMessage = $"„{name}“ ist bereits hinterlegt.";
            return;
        }

        _settings.CustomWebSources.Add(new CustomWebSource { Name = name, UrlTemplate = url });
        Persist();

        NewSourceName = string.Empty;
        NewSourceUrl = string.Empty;
        EditorMessage = $"„{name}“ hinzugefügt.";

        ReloadSources();
        SelectedSource = Sources.FirstOrDefault(s => s.Id == CustomId(name)) ?? SelectedSource;
    }

    [RelayCommand]
    private void RemoveCustomSource(CustomWebSource? source)
    {
        if (source is null)
        {
            return;
        }

        _settings.CustomWebSources.RemoveAll(s =>
            string.Equals(s.Name, source.Name, StringComparison.Ordinal) &&
            string.Equals(s.UrlTemplate, source.UrlTemplate, StringComparison.Ordinal));

        Persist();
        EditorMessage = $"„{source.Name}“ entfernt.";
        ReloadSources();
    }

    /// <summary>Fills the editor from an entry — the usual way to adapt a viewer.</summary>
    [RelayCommand]
    private void CopyToEditor(WebViewSource? source)
    {
        source ??= SelectedSource;

        if (source is null)
        {
            return;
        }

        NewSourceName = source.IsBuiltIn ? $"{source.Title} (eigene)" : source.Title;
        NewSourceUrl = source.UrlTemplate;
        EditorMessage = "Adresse übernommen — anpassen und hinzufügen.";
    }

    // -------------------------------------------------------------- updates

    /// <summary>Called by the dashboard so the viewers open at the incident.</summary>
    public void ApplyAssessment(TacticalAssessment assessment)
    {
        _latitude = assessment.Snapshot.Position.Latitude;
        _longitude = assessment.Snapshot.Position.Longitude;

        // Only reload a viewer that actually follows the position; re-navigating
        // the others would throw away whatever the operator had panned to.
        if (SelectedSource?.FollowsPosition == true)
        {
            Navigate();
        }
    }

    /// <summary>Called by the view once the browser control is ready.</summary>
    public void NotifyViewReady() => Navigate();

    /// <summary>Called by the view when navigation finished.</summary>
    public void ReportNavigationCompleted(bool success, string? detail)
    {
        IsLoading = false;

        StatusMessage = success
            ? string.Empty
            : $"Seite konnte nicht geladen werden{(detail is null ? "." : $": {detail}")} " +
              "— Verbindung auf der Registerkarte „Diagnose“ prüfen.";
    }

    /// <summary>Rebuilds the list from the catalogue plus the operator's own entries.</summary>
    public void ReloadSources()
    {
        string? previous = SelectedSource?.Id;

        _isReloading = true;

        try
        {
            RebuildManagedSources();

            Sources.Clear();

            foreach (WebSourceToggle toggle in ManagedSources.Where(t => t.IsVisible))
            {
                Sources.Add(toggle.Source);
            }

            CustomSources.Clear();

            foreach (CustomWebSource custom in _settings.CustomWebSources)
            {
                CustomSources.Add(custom);

                // A hand-edited settings file could carry anything; only navigate
                // to http(s), never to a file: or javascript: URL.
                if (!WebViewSource.IsAcceptableUrl(custom.UrlTemplate))
                {
                    continue;
                }

                Sources.Add(new WebViewSource
                {
                    Id = CustomId(custom.Name),
                    Title = string.IsNullOrWhiteSpace(custom.Name) ? custom.UrlTemplate : custom.Name,
                    Group = "Eigene Quellen",
                    UrlTemplate = custom.UrlTemplate,
                    Description = custom.UrlTemplate,
                    IsBuiltIn = false
                });
            }
        }
        finally
        {
            _isReloading = false;
        }

        if (previous is not null)
        {
            // Re-select by id: the picker holds fresh instances after a rebuild.
            SelectedSource = Sources.FirstOrDefault(s => s.Id == previous) ?? Sources.FirstOrDefault();
        }
    }

    private void RebuildManagedSources()
    {
        if (ManagedSources.Count > 0)
        {
            return;
        }

        // An empty list means "nothing hidden yet" rather than "everything off" —
        // otherwise a fresh installation would open on an empty picker.
        bool showAll = _settings.EnabledWebSourceIds.Count == 0;

        foreach (WebViewSource source in WebViewSourceCatalog.BuiltIn)
        {
            ManagedSources.Add(new WebSourceToggle(
                source,
                showAll || _settings.EnabledWebSourceIds.Contains(source.Id),
                OnVisibilityChanged));
        }
    }

    private void OnVisibilityChanged()
    {
        if (_isReloading)
        {
            return;
        }

        _settings.EnabledWebSourceIds = ManagedSources
            .Where(t => t.IsVisible)
            .Select(t => t.Source.Id)
            .ToList();

        // Every box unticked would read back as "show all" on the next start;
        // a placeholder id keeps the stored choice meaning what it says.
        if (_settings.EnabledWebSourceIds.Count == 0)
        {
            _settings.EnabledWebSourceIds = ["none"];
        }

        Persist();
        ReloadSources();
    }

    /// <summary>
    /// Adding or removing an entry is a deliberate act, so it is written at
    /// once rather than left to the next tick.
    /// </summary>
    private void Persist()
    {
        _store.SaveNow();

        if (_store.LastError is { } error)
        {
            EditorMessage = $"Einstellungen konnten nicht gespeichert werden: {error}";
        }
    }

    private static string CustomId(string name) => $"custom:{name}";

    private void Navigate()
    {
        if (SelectedSource is null)
        {
            // Reachable: every built-in can be hidden. Say so rather than leaving
            // a blank panel that looks like a failure.
            IsLoading = false;
            StatusMessage = Sources.Count == 0
                ? "Keine Ansicht ausgewählt — unter „Quellen verwalten“ eine mitgelieferte Ansicht einblenden oder eine eigene Adresse hinterlegen."
                : string.Empty;
            return;
        }

        string url = SelectedSource.Resolve(_latitude, _longitude, Zoom);

        if (!WebViewSource.IsAcceptableUrl(url))
        {
            StatusMessage = "Diese Adresse ist keine gültige http(s)-URL.";
            return;
        }

        CurrentUrl = url;
        IsLoading = true;
        StatusMessage = string.Empty;

        NavigationRequested?.Invoke(url);
    }
}
