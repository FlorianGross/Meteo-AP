using ElwMeteo.Core.Updates;
using Xunit;

namespace ElwMeteo.Core.Tests;

/// <summary>
/// Who owns the installation folder, and what follows from it.
///
/// The defect behind this file is one that only shows up weeks later. The MSI
/// package installs into a folder and records it in the Windows uninstall
/// registry; the self-update renames that folder aside and moves a new one into
/// its place, without telling Windows Installer. Everything keeps working, and
/// then: „Programme und Features" shows the old version, a repair copies the old
/// files back over the new ones, an uninstall leaves the rest behind, and the
/// next MSI upgrade installs over a folder it cannot account for.
///
/// None of that is visible at the moment it is caused, which is precisely why the
/// decision is a pure function over a made-up machine rather than something
/// checked by hand on one real one.
/// </summary>
public class InstallOriginTests
{
    private const string VehicleFolder = @"C:\Users\Wache\AppData\Local\Programs\ELW-Meteo";

    private static InstalledProduct Ours(string? location) =>
        new("ELW-Meteo", location, "1.4.0", "{11111111-2222-3333-4444-555555555555}");

    /// <summary>What an ordinary machine's uninstall list looks like around us.</summary>
    private static List<InstalledProduct> Neighbours() =>
    [
        new("7-Zip 24.08", @"C:\Program Files\7-Zip", "24.08", "{aaaa}"),
        new("Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application", "131.0", "{bbbb}"),
        new("Notepad++", @"C:\Program Files\Notepad++", "8.7", "{cccc}"),
        // Patches and components carry no folder at all.
        new("Sicherheitsupdate für Windows", null, null, "{dddd}")
    ];

    [Fact]
    public void AnUnpackedArchiveIsPortable()
    {
        Assert.Equal(
            InstallOrigin.Portable,
            InstallOriginDetector.Detect(Neighbours(), @"D:\Werkzeuge\ELW-Meteo"));
    }

    [Fact]
    public void WithoutARegistryEverythingIsPortable()
    {
        // Linux and macOS, and a Windows machine whose hives could not be read.
        Assert.Equal(InstallOrigin.Portable, InstallOriginDetector.Detect(null, VehicleFolder));
        Assert.Equal(InstallOrigin.Portable, InstallOriginDetector.Detect([], VehicleFolder));
    }

    [Fact]
    public void AnMsiInstallationIsRecognisedByItsFolder()
    {
        List<InstalledProduct> products = [.. Neighbours(), Ours(VehicleFolder)];

        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder));
    }

    [Fact]
    public void ATrailingSeparatorDoesNotHideTheOwner()
    {
        // Windows Installer writes InstallLocation with a trailing backslash more
        // often than not, and AppContext.BaseDirectory always has one. Comparing
        // the two raw strings would answer "portable" on every MSI installation —
        // which is the failure this whole file is about, arrived at by accident.
        List<InstalledProduct> products = [Ours(VehicleFolder + @"\")];

        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder));
        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder + @"\"));
    }

    [Fact]
    public void TheFolderIsMatchedWithoutRegardForCase()
    {
        List<InstalledProduct> products = [Ours(VehicleFolder.ToUpperInvariant())];

        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder));
    }

    [Fact]
    public void QuotesAroundThePathAreIgnored()
    {
        List<InstalledProduct> products = [Ours($"\"{VehicleFolder}\"")];

        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder));
    }

    [Fact]
    public void ADifferentCopyOfTheSameApplicationIsNotTheOwner()
    {
        // The machine has an MSI installation and somebody also unpacked a ZIP
        // elsewhere. The ZIP copy must swap its own folder, not conclude that
        // Windows Installer owns it.
        List<InstalledProduct> products = [Ours(VehicleFolder)];

        Assert.Equal(InstallOrigin.Portable,
            InstallOriginDetector.Detect(products, @"D:\Stick\ELW-Meteo"));
    }

    [Fact]
    public void AnEmptyInstallLocationNeverMatches()
    {
        // Plenty of uninstall entries have an empty InstallLocation. Treating
        // empty as a match would make every portable copy look MSI-managed.
        List<InstalledProduct> products = [Ours(string.Empty), Ours(null), Ours("   ")];

        Assert.Equal(InstallOrigin.Portable, InstallOriginDetector.Detect(products, VehicleFolder));
        Assert.Equal(InstallOrigin.Portable, InstallOriginDetector.Detect(products, string.Empty));
    }

    [Fact]
    public void TheOwnerIsReturnedWithItsVersion()
    {
        List<InstalledProduct> products = [.. Neighbours(), Ours(VehicleFolder)];

        InstalledProduct? owner = InstallOriginDetector.FindOwner(products, VehicleFolder);

        Assert.NotNull(owner);
        Assert.Equal("1.4.0", owner!.DisplayVersion);
    }

    [Fact]
    public void ARenamedProductIsStillTheOwner()
    {
        // Matching on the folder rather than the display name: the name is a
        // string a rebranding or a translation can change, the folder is the
        // thing a swap would actually damage.
        List<InstalledProduct> products =
            [new("ELW-Meteo (Feuerwehr Musterstadt)", VehicleFolder, "1.4.0", "{eeee}")];

        Assert.Equal(InstallOrigin.WindowsInstaller,
            InstallOriginDetector.Detect(products, VehicleFolder));
    }
}

public class UpdateRoutingTests
{
    private static ReleaseInfo Release(params string[] assetNames) => new()
    {
        Version = new AppVersion(1, 5, 0, null),
        TagName = "v1.5.0",
        Assets = [.. assetNames.Select(n => new ReleaseAsset
        {
            Name = n,
            DownloadUrl = $"https://example.invalid/{n}"
        })]
    };

    private static ReleaseInfo FullRelease() => Release(
        "ELW-Meteo-1.5.0-win-x64.zip",
        "ELW-Meteo-1.5.0-win-x64-standalone.zip",
        "ELW-Meteo-1.5.0-linux-x64.zip",
        "ELW-Meteo-1.5.0-macos-x64.zip",
        "ELW-Meteo-1.5.0-macos-arm64.zip",
        "ELW-Meteo-1.5.0-win-x64.msi");

    [Fact]
    public void APortableCopySwapsItsFolder()
    {
        Assert.Equal(
            UpdateRoute.FolderSwap,
            UpdateRouting.Choose(InstallOrigin.Portable, FullRelease()));
    }

    [Fact]
    public void AnMsiInstallationTakesTheMsi()
    {
        Assert.Equal(
            UpdateRoute.WindowsInstallerPackage,
            UpdateRouting.Choose(InstallOrigin.WindowsInstaller, FullRelease()));
    }

    [Fact]
    public void AnMsiInstallationNeverFallsBackToTheFolderSwap()
    {
        // The case this whole mechanism exists for. The release carries every ZIP
        // but no MSI; swapping the folder would work mechanically and leave
        // Windows Installer describing an installation that is no longer there.
        ReleaseInfo withoutMsi = Release(
            "ELW-Meteo-1.5.0-win-x64.zip",
            "ELW-Meteo-1.5.0-win-x64-standalone.zip");

        Assert.Equal(
            UpdateRoute.ReleasePageOnly,
            UpdateRouting.Choose(InstallOrigin.WindowsInstaller, withoutMsi));
    }

    [Fact]
    public void WithoutAReleaseThereIsNothingToRouteTo()
    {
        Assert.Equal(
            UpdateRoute.ReleasePageOnly,
            UpdateRouting.Choose(InstallOrigin.WindowsInstaller, null));

        // A portable copy with no release is simply not offered an update; the
        // route it would take if there were one is still the swap.
        Assert.Equal(UpdateRoute.FolderSwap, UpdateRouting.Choose(InstallOrigin.Portable, null));
    }

    [Fact]
    public void TheInstallerIsPickedByItsExtension()
    {
        ReleaseAsset? msi = UpdatePackageSelector.SelectInstaller(FullRelease());

        Assert.NotNull(msi);
        Assert.EndsWith(".msi", msi!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AZipIsNeverMistakenForAnInstaller()
    {
        // ".msi" has to be the extension, not just somewhere in the name.
        ReleaseInfo misleading = Release("ELW-Meteo-1.5.0-msi-quelltext.zip");

        Assert.Null(UpdatePackageSelector.SelectInstaller(misleading));
    }

    [Fact]
    public void EveryRouteIsExplainedInWords()
    {
        foreach (UpdateRoute route in Enum.GetValues<UpdateRoute>())
        {
            string text = UpdateRouting.Describe(route);

            Assert.False(string.IsNullOrWhiteSpace(text));
            // Each explanation has to say what will happen, not just that
            // something will — the panel shows this instead of a progress bar.
            Assert.True(text.Length > 60, $"{route} ist zu knapp erklärt.");
        }
    }
}

public class WindowsInstallerScriptTests
{
    private static readonly AppVersion Version = new(1, 5, 0, null);

    private static string Script() => UpdateInstaller.BuildWindowsInstallerScript(
        @"C:\Users\Wache\AppData\Roaming\ELW-Meteo\Update\download\ELW-Meteo-1.5.0-win-x64.msi",
        @"C:\Users\Wache\AppData\Local\Programs\ELW-Meteo",
        "ELW-Meteo.exe",
        Version,
        4242,
        @"C:\Users\Wache\AppData\Roaming\ELW-Meteo\Update\apply-update.log");

    [Fact]
    public void ItWaitsForTheRunningProcess()
    {
        // Starting msiexec while the executable is still locked is how an upgrade
        // turns into a files-in-use prompt nobody is standing in front of.
        string script = Script();

        Assert.Contains("PID eq 4242", script, StringComparison.Ordinal);
        Assert.Contains("goto wait", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ItHandsTheWorkToWindowsInstaller()
    {
        string script = Script();

        Assert.Contains("msiexec /i", script, StringComparison.Ordinal);
        Assert.Contains("ELW-Meteo-1.5.0-win-x64.msi", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ItTouchesNothingInTheInstallation()
    {
        // The entire point of this route. A move, a rename or a delete here would
        // put back exactly the divergence it exists to prevent.
        string script = Script();

        Assert.DoesNotContain("move ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rmdir", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" del ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("robocopy", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItIsNotSilent()
    {
        string script = Script();

        // /passive shows progress and needs no clicks. /qn would replace the
        // application on a vehicle screen with no sign of it, and a silent
        // install that hits a problem leaves nothing on screen at all.
        Assert.Contains("/passive", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/qn", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ItWritesItsOwnInstallerLog()
    {
        string script = Script();

        Assert.Contains("/l*v", script, StringComparison.Ordinal);
        Assert.Contains("apply-update.msi.log", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryQuotedPathIsQuoted()
    {
        string script = Script();

        // Both paths contain no space here, but the install folder of a user
        // called "Max Mustermann" does, and the one that needed quoting must not
        // be the one that was missed.
        Assert.Contains("\"C:\\Users\\Wache\\AppData\\Roaming\\ELW-Meteo\\Update\\download\\ELW-Meteo-1.5.0-win-x64.msi\"",
            script, StringComparison.Ordinal);
        Assert.Contains("\"C:\\Users\\Wache\\AppData\\Local\\Programs\\ELW-Meteo\\ELW-Meteo.exe\"",
            script, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedUpgradeStartsTheOldVersionAgain()
    {
        string script = Script();

        // The old installation is untouched either way, so leaving the operator
        // with no application at all would be a self-inflicted outage.
        Assert.Contains("NEQ 0", script, StringComparison.Ordinal);
        Assert.Contains("goto restart", script, StringComparison.Ordinal);
        Assert.Contains("start \"\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ARebootRequestIsNotTreatedAsAFailure()
    {
        // 3010 means "done, reboot recommended". Reporting it as a failure would
        // have somebody chasing a successful update.
        Assert.Contains("3010", Script(), StringComparison.Ordinal);
    }

    [Fact]
    public void ItUsesWindowsSeparatorsThroughout()
    {
        string script = Script();

        Assert.DoesNotContain("ELW-Meteo/ELW-Meteo.exe", script, StringComparison.Ordinal);
    }
}
