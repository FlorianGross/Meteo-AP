using System.Runtime.Versioning;
using ElwMeteo.Core.Updates;

namespace ElwMeteo.Presentation.Platform;

/// <summary>
/// What the system says is installed, so the update can tell whether Windows
/// Installer owns the folder it is running from.
///
/// An interface because only Windows has an answer, and because the decision that
/// depends on it (<see cref="InstallOriginDetector"/>) has to be testable against
/// a made-up machine rather than against whatever happens to be installed on the
/// build agent.
/// </summary>
public interface IInstalledProductRegistry
{
    /// <summary>Everything listed under „Programme und Features", or empty.</summary>
    IReadOnlyList<InstalledProduct> List();
}

/// <summary>Answers „nothing" — correct on Linux and macOS, which have no MSI.</summary>
public sealed class NoInstalledProductRegistry : IInstalledProductRegistry
{
    public IReadOnlyList<InstalledProduct> List() => [];
}

/// <summary>
/// Reads the Windows uninstall registry.
///
/// All four places are searched, because which one an entry lands in depends on
/// how the package was installed: a per-user package writes under
/// <c>HKEY_CURRENT_USER</c>, a per-machine one under <c>HKEY_LOCAL_MACHINE</c>,
/// and a 32-bit package on a 64-bit system lands in the <c>WOW6432Node</c>
/// branch of either. The MSI of this application is per-user, but that is a
/// decision that could change and an installation from an older package could
/// still be sitting in another hive — so looking in one place would be an
/// assumption, not a reading.
/// </summary>
public sealed class WindowsInstalledProductRegistry : IInstalledProductRegistry
{
    private const string UninstallPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string UninstallPathWow =
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstalledProduct> List()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return ReadAll();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<InstalledProduct> ReadAll()
    {
        List<InstalledProduct> products = [];

        foreach ((Microsoft.Win32.RegistryKey root, string path) in Roots())
        {
            try
            {
                using Microsoft.Win32.RegistryKey? parent = root.OpenSubKey(path, writable: false);

                if (parent is null)
                {
                    continue;
                }

                foreach (string name in parent.GetSubKeyNames())
                {
                    if (TryRead(parent, name) is { } product)
                    {
                        products.Add(product);
                    }
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // One unreadable hive must not hide the others. A locked-down
                // machine where nothing can be read answers "portable", which is
                // the cautious direction only for the folder swap — so the caller
                // is the one that decides, not this.
            }
        }

        return products;
    }

    [SupportedOSPlatform("windows")]
    private static InstalledProduct? TryRead(Microsoft.Win32.RegistryKey parent, string name)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = parent.OpenSubKey(name, writable: false);

            if (key is null)
            {
                return null;
            }

            string? display = key.GetValue("DisplayName") as string;

            // Entries without a display name are patches and components, not
            // products; they never carry an InstallLocation either.
            if (string.IsNullOrWhiteSpace(display))
            {
                return null;
            }

            return new InstalledProduct(
                display,
                key.GetValue("InstallLocation") as string,
                key.GetValue("DisplayVersion") as string,
                // The key name is the product code for anything installed by
                // Windows Installer; for other entries it is just a name, and
                // the caller does not use it in that case.
                name);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<(Microsoft.Win32.RegistryKey Root, string Path)> Roots()
    {
        yield return (Microsoft.Win32.Registry.CurrentUser, UninstallPath);
        yield return (Microsoft.Win32.Registry.CurrentUser, UninstallPathWow);
        yield return (Microsoft.Win32.Registry.LocalMachine, UninstallPath);
        yield return (Microsoft.Win32.Registry.LocalMachine, UninstallPathWow);
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or
              System.Security.SecurityException or PlatformNotSupportedException;
}
