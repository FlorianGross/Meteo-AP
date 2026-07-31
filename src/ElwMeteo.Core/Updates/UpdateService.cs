using System.Reflection;
using ElwMeteo.Core.Configuration;

namespace ElwMeteo.Core.Updates;

/// <summary>What a check found.</summary>
public enum UpdateAvailability
{
    /// <summary>Not looked yet.</summary>
    Unknown,
    UpToDate,
    Available,
    /// <summary>A newer release exists but carries no package for this platform.</summary>
    NoPackageForPlatform,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    AppVersion Current,
    ReleaseInfo? Release,
    ReleaseAsset? Asset,
    string Message)
{
    public bool CanInstall => Availability == UpdateAvailability.Available && Asset is not null;
}

/// <summary>
/// Holds the three steps of an update apart: look, fetch, swap.
///
/// They are separate on purpose. Looking happens by itself if the operator
/// allowed it; fetching and swapping never do. On a vehicle that is not a
/// nicety — an application that restarts itself while an incident is running is
/// worse than an out-of-date one.
/// </summary>
public sealed class UpdateService(
    GitHubReleaseProvider releases,
    UpdateDownloader downloader,
    UpdateInstaller installer)
{
    /// <summary>
    /// The running version, read from the assembly. Release builds get it from
    /// the workflow's -p:Version; a local build reports 1.0.0, which is why a
    /// development copy is told rather than offered an update.
    /// </summary>
    public static AppVersion CurrentVersion
    {
        get
        {
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version;

            return version is null
                ? AppVersion.Zero
                : new AppVersion(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build, null);
        }
    }

    /// <summary>Everything this feature writes lives here, never in the installation.</summary>
    public static string WorkingDirectory => Path.Combine(AppSettings.DefaultDirectory, "Update");

    public async Task<UpdateCheckResult> CheckAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        AppVersion current = CurrentVersion;
        string repository = string.IsNullOrWhiteSpace(settings.UpdateRepository)
            ? GitHubReleaseProvider.DefaultRepository
            : settings.UpdateRepository;

        try
        {
            ReleaseInfo? release = await releases
                .GetLatestAsync(repository, settings.UpdateIncludePreReleases, cancellationToken)
                .ConfigureAwait(false);

            if (release is null)
            {
                return new UpdateCheckResult(UpdateAvailability.UpToDate, current, null, null,
                    $"Das Repository {repository} hat noch keine Freigabe mit Versionsnummer.");
            }

            if (release.Version <= current)
            {
                return new UpdateCheckResult(UpdateAvailability.UpToDate, current, release, null,
                    $"Aktuell — installiert ist {current}, neuste Freigabe ist {release.Version}.");
            }

            UpdatePlatform platform = UpdatePackageSelector.CurrentPlatform;
            ReleaseAsset? asset = UpdatePackageSelector.Select(release, platform);

            if (asset is null)
            {
                return new UpdateCheckResult(UpdateAvailability.NoPackageForPlatform, current, release, null,
                    $"Version {release.Version} ist verfügbar, enthält aber kein Paket für " +
                    $"{UpdatePackageSelector.Describe(platform)}. Die Freigabeseite führt auf, was es gibt.");
            }

            return new UpdateCheckResult(UpdateAvailability.Available, current, release, asset,
                $"Version {release.Version} ist verfügbar — installiert ist {current}.");
        }
        catch (UpdateCheckException ex)
        {
            return new UpdateCheckResult(UpdateAvailability.Failed, current, null, null, ex.Message);
        }
    }

    /// <summary>Fetches and verifies the package. Nothing is installed yet.</summary>
    public async Task<string> DownloadAsync(ReleaseAsset asset, CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(WorkingDirectory, "download");

        // A leftover partial archive from an earlier attempt must not be reused.
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Overwritten anyway; a stale sibling file is harmless.
            }
        }

        return await downloader.DownloadAsync(asset, directory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unpacks and checks the package, still without touching the installation.</summary>
    public StagedUpdate Stage(string archivePath, AppVersion version)
    {
        string installDirectory = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        InstallLocation location = InstallLocation.Inspect(installDirectory);

        if (!location.IsWritable)
        {
            throw new UpdateInstallException(location.Reason ??
                "Das Installationsverzeichnis ist nicht beschreibbar.");
        }

        return installer.Stage(archivePath, version, installDirectory, WorkingDirectory);
    }

    /// <summary>
    /// Starts the swap. The caller must close the application right after — the
    /// script waits for this process and does nothing until it is gone.
    /// </summary>
    public void Apply(StagedUpdate staged) =>
        installer.Apply(staged, WorkingDirectory, Environment.ProcessId);

    /// <summary>Whether a check should run by itself right now.</summary>
    public static bool ShouldCheckAutomatically(AppSettings settings, DateTimeOffset now)
    {
        if (!settings.UpdateCheckEnabled)
        {
            return false;
        }

        // GitHub allows sixty unauthenticated requests per hour per address;
        // once a day is plenty for a release cadence measured in weeks.
        return settings.LastUpdateCheckUtc is not { } last ||
               now - last >= TimeSpan.FromHours(Math.Clamp(settings.UpdateCheckIntervalHours, 1, 720));
    }
}
