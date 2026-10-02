namespace ElwMeteo.Core.Updates;

/// <summary>How the running copy got onto the machine.</summary>
public enum InstallOrigin
{
    /// <summary>Unpacked from a ZIP, or built here. Nothing else knows about it.</summary>
    Portable,

    /// <summary>Installed by the MSI package, so Windows Installer owns the folder.</summary>
    WindowsInstaller
}

/// <summary>How an update would be applied.</summary>
public enum UpdateRoute
{
    /// <summary>Unpack beside the installation and rename it in on restart.</summary>
    FolderSwap,

    /// <summary>Download the new MSI and let Windows Installer do the upgrade.</summary>
    WindowsInstallerPackage,

    /// <summary>Neither is safe; the operator has to fetch the package by hand.</summary>
    ReleasePageOnly
}

/// <summary>
/// One entry of the Windows uninstall registry, as the platform read it.
///
/// A record rather than a registry key so the decision below can be tested: the
/// case that matters is a machine with two dozen unrelated products installed,
/// and that is not something to reproduce by writing to a real registry.
/// </summary>
public sealed record InstalledProduct(
    string DisplayName,
    string? InstallLocation,
    string? DisplayVersion,
    string? ProductCode);

/// <summary>
/// Whether Windows Installer owns the folder the application is running from.
///
/// This exists because the self-update and the MSI package disagree about who
/// owns the installation, and the disagreement is silent. The update renames the
/// installation aside and moves a new folder into its place; Windows Installer
/// knows nothing of it and goes on believing the old version is there, with the
/// old file list. The consequences all arrive later, which is what makes them
/// expensive:
///
///   * „Programme und Features" keeps showing the version from before the update
///   * a repair (<c>msiexec /f</c>) copies the old files back over the new ones
///   * an uninstall removes what the package knows and leaves the rest behind,
///     including the <c>.vor-1.2.3</c> folder the swap kept as a safety net
///   * the next MSI upgrade installs over a folder whose contents it cannot
///     account for
///
/// So on an MSI installation the folder is not swapped at all: the new MSI is
/// downloaded and Windows Installer performs its own upgrade, which keeps the
/// package database and the folder describing the same thing.
/// </summary>
public static class InstallOriginDetector
{
    /// <summary>
    /// Finds the installed product whose folder is the one being run from, or
    /// null when none is. Matching on the folder rather than on the product name
    /// is deliberate: the name is a display string that a rebranding or a
    /// localisation can change, while the folder is the thing the swap would
    /// actually damage.
    /// </summary>
    public static InstalledProduct? FindOwner(
        IReadOnlyList<InstalledProduct>? products,
        string? installDirectory)
    {
        if (products is null || products.Count == 0 || string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        string wanted = Normalise(installDirectory);

        return products.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.InstallLocation) &&
            string.Equals(Normalise(p.InstallLocation), wanted, StringComparison.OrdinalIgnoreCase));
    }

    public static InstallOrigin Detect(
        IReadOnlyList<InstalledProduct>? products,
        string? installDirectory) =>
        FindOwner(products, installDirectory) is null
            ? InstallOrigin.Portable
            : InstallOrigin.WindowsInstaller;

    /// <summary>
    /// Trims the trailing separator and nothing else. No full-path resolution on
    /// purpose: this runs against strings a registry handed over, and
    /// <c>Path.GetFullPath</c> would resolve them against the current working
    /// directory of whatever process is asking.
    /// </summary>
    internal static string Normalise(string? path) =>
        (path ?? string.Empty).Trim().Trim('"').TrimEnd('/', '\\');
}

/// <summary>Which of the two ways an update can be applied, given what is installed.</summary>
public static class UpdateRouting
{
    /// <summary>
    /// The route for this installation and this release.
    ///
    /// The one case worth spelling out is an MSI installation and a release with
    /// no MSI attached. Falling back to the folder swap there would be the
    /// corruption this whole file exists to avoid, so the answer is to send the
    /// operator to the release page instead. An update that does not happen is a
    /// nuisance; an installation Windows Installer can no longer account for is
    /// a machine somebody has to take apart by hand.
    /// </summary>
    public static UpdateRoute Choose(InstallOrigin origin, ReleaseInfo? release) =>
        origin switch
        {
            InstallOrigin.WindowsInstaller when release is null => UpdateRoute.ReleasePageOnly,
            InstallOrigin.WindowsInstaller =>
                UpdatePackageSelector.SelectInstaller(release!) is null
                    ? UpdateRoute.ReleasePageOnly
                    : UpdateRoute.WindowsInstallerPackage,
            _ => UpdateRoute.FolderSwap
        };

    /// <summary>What the update panel says about the route it is going to take.</summary>
    public static string Describe(UpdateRoute route) => route switch
    {
        UpdateRoute.WindowsInstallerPackage =>
            "Diese Installation stammt aus dem MSI-Paket. Die Aktualisierung läuft deshalb " +
            "über ein neues MSI-Paket und nicht über einen Ordnertausch — nur so bleiben " +
            "Verzeichnis und Eintrag in „Programme und Features\" einig darüber, was " +
            "installiert ist.",
        UpdateRoute.FolderSwap =>
            "Diese Installation ist ein entpacktes Archiv. Die neue Version wird daneben " +
            "entpackt und beim Neustart an die Stelle der alten geschoben; die alte bleibt " +
            "als Ordner daneben liegen.",
        _ =>
            "Diese Installation stammt aus dem MSI-Paket, diese Freigabe enthält aber kein " +
            "MSI-Paket. Ein Ordnertausch würde die Installation hinter dem Rücken von " +
            "Windows Installer verändern und wird deshalb nicht angeboten — das Paket bitte " +
            "von der Freigabeseite laden."
    };
}
