using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Updates;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.ViewModels;

/// <summary>
/// The update panel.
///
/// Three steps, three clicks, in this order: prüfen, herunterladen, einspielen.
/// Nothing collapses them. An application that fetches and restarts itself on
/// its own is a liability on a vehicle — the one moment it decides to do that
/// will be the moment someone is reading a wind direction off it.
///
/// The installation itself is deliberately the last and smallest step: by the
/// time it runs, the package is downloaded, its checksum is verified and it is
/// unpacked and checked for completeness. All that happens while the running
/// application is still intact.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly IShellLauncher _shell;

    /// <summary>Raised when the shell must shut down so the swap can proceed.</summary>
    public event Action? RestartRequested;

    private string? _downloadedArchive;
    private StagedUpdate? _staged;
    private UpdateCheckResult? _lastResult;

    public UpdateViewModel(
        UpdateService updates,
        SettingsStore store,
        IShellLauncher shell,
        UpdateDownloader downloader)
    {
        _updates = updates;
        _store = store;
        _settings = store.Settings;
        _shell = shell;

        _updateCheckEnabled = _settings.UpdateCheckEnabled;
        _includePreReleases = _settings.UpdateIncludePreReleases;
        _repository = _settings.UpdateRepository;

        downloader.ProgressChanged += fraction => DownloadProgress = fraction ?? 0;
    }

    public string CurrentVersionLabel => $"Installiert: {UpdateService.CurrentVersion}";

    public string PlatformLabel =>
        $"Plattform: {UpdatePackageSelector.Describe(UpdatePackageSelector.CurrentPlatform)}";

    [ObservableProperty]
    private string _status = "Noch nicht geprüft.";

    [ObservableProperty]
    private string _releaseTitle = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private string _packageLabel = string.Empty;

    /// <summary>Set when GitHub gave no checksum, so the operator knows it was skipped.</summary>
    [ObservableProperty]
    private string _integrityNote = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isReadyToInstall;

    [ObservableProperty]
    private string _releaseUrl = string.Empty;

    // -------------------------------------------------------- settings copy

    [ObservableProperty]
    private bool _updateCheckEnabled;

    [ObservableProperty]
    private bool _includePreReleases;

    [ObservableProperty]
    private string _repository = GitHubReleaseProvider.DefaultRepository;

    partial void OnUpdateCheckEnabledChanged(bool value)
    {
        _settings.UpdateCheckEnabled = value;
        _store.RequestSave();
    }

    partial void OnIncludePreReleasesChanged(bool value)
    {
        _settings.UpdateIncludePreReleases = value;
        _store.RequestSave();
    }

    partial void OnRepositoryChanged(string value)
    {
        _settings.UpdateRepository = value.Trim();
        _store.RequestSave();
    }

    // ------------------------------------------------------------- commands

    /// <summary>Step 1 — ask GitHub what the newest release is.</summary>
    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Reset();
        Status = "Freigaben werden abgefragt …";

        try
        {
            UpdateCheckResult result = await _updates
                .CheckAsync(_settings, cancellationToken)
                .ConfigureAwait(true);

            _lastResult = result;
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _store.RequestSave();

            Status = result.Message;
            ReleaseTitle = result.Release?.Title ?? string.Empty;
            ReleaseNotes = Shorten(result.Release?.Notes);
            ReleaseUrl = result.Release?.HtmlUrl ?? string.Empty;

            IsUpdateAvailable = result.CanInstall;

            if (result.Asset is { } asset)
            {
                PackageLabel = $"{asset.Name} ({asset.Size / 1024.0 / 1024.0:F1} MB)";

                IntegrityNote = asset.Sha256 is null
                    ? "GitHub meldet für dieses Paket keine Prüfsumme — es wird nur die Größe geprüft."
                    : "Die Prüfsumme wird nach dem Download gegen die von GitHub gemeldete geprüft.";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Prüfung abgebrochen.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Step 2 — fetch and verify. Still nothing installed.</summary>
    [RelayCommand]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        UpdateCheckResult? pending = _lastResult;

        if (pending?.Asset is not { } asset || pending.Release is null)
        {
            Status = "Erst prüfen, dann herunterladen.";
            return;
        }

        IsBusy = true;
        DownloadProgress = 0;
        Status = "Paket wird geladen …";

        try
        {
            _downloadedArchive = await _updates
                .DownloadAsync(asset, cancellationToken)
                .ConfigureAwait(true);

            // Unpacking and the completeness check happen now, not at restart:
            // a bad package must fail while the running application is intact.
            _staged = _updates.Stage(_downloadedArchive, pending.Release.Version);

            IsDownloaded = true;
            IsReadyToInstall = true;
            Status = $"Version {pending.Release.Version} ist geladen, geprüft und entpackt. " +
                     "Das Einspielen beendet die Anwendung und startet sie neu.";
        }
        catch (OperationCanceledException)
        {
            Status = "Download abgebrochen.";
        }
        catch (Exception ex) when (ex is UpdateDownloadException or UpdateInstallException)
        {
            Status = ex.Message;
            IsDownloaded = false;
            IsReadyToInstall = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Step 3 — swap and restart. The only step that touches the installation.</summary>
    [RelayCommand]
    private void InstallAndRestart()
    {
        if (_staged is not { } staged)
        {
            Status = "Es liegt kein geprüftes Paket bereit.";
            return;
        }

        try
        {
            // Pending settings first: the process is about to end.
            _store.Flush();

            _updates.Apply(staged);

            Status = "Die Anwendung wird beendet und in der neuen Version gestartet.";
            RestartRequested?.Invoke();
        }
        catch (UpdateInstallException ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Do not offer this version again.</summary>
    [RelayCommand]
    private void SkipVersion()
    {
        if (_lastResult?.Release is { } release)
        {
            _settings.SkippedUpdateVersion = release.Version.ToString();
            _store.RequestSave();
            Status = $"Version {release.Version} wird nicht mehr angeboten.";
        }

        Reset();
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleaseUrl))
        {
            return;
        }

        if (!_shell.TryOpen(ReleaseUrl, out string? error))
        {
            Status = $"Freigabeseite konnte nicht geöffnet werden: {error}";
        }
    }

    // -------------------------------------------------------------- updates

    /// <summary>
    /// Runs a check without being asked, if the operator allowed it and enough
    /// time has passed. Never downloads.
    /// </summary>
    public async Task CheckSilentlyAsync(CancellationToken cancellationToken = default)
    {
        if (!UpdateService.ShouldCheckAutomatically(_settings, DateTimeOffset.UtcNow))
        {
            return;
        }

        await CheckAsync(cancellationToken).ConfigureAwait(true);

        // A version the operator dismissed stays dismissed.
        if (_lastResult?.Release is { } release &&
            string.Equals(_settings.SkippedUpdateVersion, release.Version.ToString(), StringComparison.Ordinal))
        {
            IsUpdateAvailable = false;
        }
    }

    private void Reset()
    {
        IsUpdateAvailable = false;
        IsDownloaded = false;
        IsReadyToInstall = false;
        DownloadProgress = 0;
        ReleaseTitle = string.Empty;
        ReleaseNotes = string.Empty;
        PackageLabel = string.Empty;
        IntegrityNote = string.Empty;
        ReleaseUrl = string.Empty;
        _downloadedArchive = null;
        _staged = null;
    }

    /// <summary>Release notes can be long; the panel shows the head of them.</summary>
    internal static string Shorten(string? notes, int limit = 1200)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        string trimmed = notes.Trim();

        return trimmed.Length <= limit
            ? trimmed
            : trimmed[..limit] + "\n\n… vollständige Notizen auf der Freigabeseite.";
    }
}
