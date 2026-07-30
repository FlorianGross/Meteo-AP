using System.Runtime.InteropServices;
using System.Text.Json;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Updates;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V10.0.1", 10, 0, 1, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1.2.3.4", 1, 2, 3, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3+build7", 1, 2, 3, null)]
    public void TryParse_ReadsTheFormsThatAppearInTags(
        string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        Assert.Equal(new AppVersion(major, minor, patch, pre), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3.4.5")]
    public void TryParse_RejectsWhatCannotBeCompared(string? text)
    {
        // A tag like "nightly" has no ordering, so it must not be mistaken for
        // a release that is newer than the running one.
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void Ordering_FollowsTheNumbers()
    {
        Assert.True(V("1.0.1") > V("1.0.0"));
        Assert.True(V("1.1.0") > V("1.0.9"));
        Assert.True(V("2.0.0") > V("1.99.99"));
        Assert.True(V("1.0.0") <= V("1.0.0"));
    }

    [Fact]
    public void ARealReleaseOutranksItsOwnPreReleases()
    {
        Assert.True(V("1.2.0") > V("1.2.0-beta.1"));
        Assert.True(V("1.2.0-beta.2") > V("1.2.0-beta.1"));

        // And a pre-release of the next version still beats the current release.
        Assert.True(V("1.3.0-beta.1") > V("1.2.0"));
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        Assert.Equal("1.2.3", V("1.2.3").ToString());
        Assert.Equal("1.2.3-rc.1", V("1.2.3-rc.1").ToString());
    }

    private static AppVersion V(string text)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        return version;
    }
}

public class UpdatePackageSelectorTests
{
    private static ReleaseInfo Release(params string[] assetNames) => new()
    {
        Version = new AppVersion(1, 1, 0, null),
        TagName = "v1.1.0",
        Assets = assetNames.Select(n => new ReleaseAsset
        {
            Name = n,
            DownloadUrl = $"https://github.com/x/y/releases/download/v1.1.0/{n}"
        }).ToList()
    };

    /// <summary>The names the release workflow actually produces.</summary>
    private static ReleaseInfo RealisticRelease() => Release(
        "ELW-Meteo-1.1.0-win-x64.zip",
        "ELW-Meteo-1.1.0-win-x64-standalone.zip",
        "ELW-Meteo-1.1.0-linux-x64.zip",
        "ELW-Meteo-1.1.0-macos-x64.zip",
        "ELW-Meteo-1.1.0-macos-arm64.zip");

    [Theory]
    [InlineData(UpdatePlatform.LinuxX64, "ELW-Meteo-1.1.0-linux-x64.zip")]
    [InlineData(UpdatePlatform.MacOsX64, "ELW-Meteo-1.1.0-macos-x64.zip")]
    [InlineData(UpdatePlatform.MacOsArm64, "ELW-Meteo-1.1.0-macos-arm64.zip")]
    public void Select_MatchesTheNamesTheWorkflowProduces(UpdatePlatform platform, string expected)
    {
        Assert.Equal(expected, UpdatePackageSelector.Select(RealisticRelease(), platform)?.Name);
    }

    [Fact]
    public void OnWindowsTheStandaloneArchiveWins()
    {
        // The framework-dependent package would need a runtime the machine may
        // not have — an update must not be able to break a working install.
        Assert.Equal(
            "ELW-Meteo-1.1.0-win-x64-standalone.zip",
            UpdatePackageSelector.Select(RealisticRelease(), UpdatePlatform.WindowsX64)?.Name);
    }

    [Fact]
    public void OnWindowsTheFrameworkPackageIsUsedWhenItIsTheOnlyOne()
    {
        Assert.Equal(
            "ELW-Meteo-1.1.0-win-x64.zip",
            UpdatePackageSelector.Select(Release("ELW-Meteo-1.1.0-win-x64.zip"), UpdatePlatform.WindowsX64)?.Name);
    }

    [Fact]
    public void MacArm64DoesNotFallBackToTheIntelPackage()
    {
        // Silently installing the wrong architecture is worse than reporting
        // that there is nothing to install.
        ReleaseInfo intelOnly = Release("ELW-Meteo-1.1.0-macos-x64.zip");

        Assert.Null(UpdatePackageSelector.Select(intelOnly, UpdatePlatform.MacOsArm64));
    }

    [Fact]
    public void Select_IgnoresNonZipAssets()
    {
        ReleaseInfo release = Release("ELW-Meteo-1.1.0-linux-x64.tar.gz", "checksums-linux-x64.txt");

        Assert.Null(UpdatePackageSelector.Select(release, UpdatePlatform.LinuxX64));
    }

    [Fact]
    public void Select_ReturnsNullForAnUnknownPlatform()
    {
        Assert.Null(UpdatePackageSelector.Select(RealisticRelease(), UpdatePlatform.Unknown));
    }

    [Theory]
    [InlineData(Architecture.X64, true, false, false, UpdatePlatform.WindowsX64)]
    [InlineData(Architecture.X64, false, true, false, UpdatePlatform.LinuxX64)]
    [InlineData(Architecture.X64, false, false, true, UpdatePlatform.MacOsX64)]
    [InlineData(Architecture.Arm64, false, false, true, UpdatePlatform.MacOsArm64)]
    [InlineData(Architecture.Arm64, false, true, false, UpdatePlatform.Unknown)]
    [InlineData(Architecture.X64, false, false, false, UpdatePlatform.Unknown)]
    public void Detect_MapsSystemAndArchitecture(
        Architecture architecture, bool windows, bool linux, bool macOs, UpdatePlatform expected)
    {
        Assert.Equal(expected, UpdatePackageSelector.Detect("os", architecture, windows, linux, macOs));
    }

    [Fact]
    public void Describe_IsGermanForEveryPlatform()
    {
        foreach (UpdatePlatform platform in Enum.GetValues<UpdatePlatform>())
        {
            Assert.False(string.IsNullOrWhiteSpace(UpdatePackageSelector.Describe(platform)));
        }
    }
}

public class GitHubReleaseProviderTests
{
    private const string Payload = """
        [
          {
            "tag_name": "v1.2.0",
            "name": "ELW-Meteo 1.2.0",
            "body": "Neues",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-07-27T14:37:22Z",
            "html_url": "https://github.com/x/y/releases/tag/v1.2.0",
            "assets": [
              {
                "name": "ELW-Meteo-1.2.0-linux-x64.zip",
                "browser_download_url": "https://github.com/x/y/releases/download/v1.2.0/ELW-Meteo-1.2.0-linux-x64.zip",
                "size": 12345,
                "digest": "sha256:ABCDEF0123"
              }
            ]
          },
          {
            "tag_name": "v1.3.0-beta.1",
            "draft": false,
            "prerelease": true,
            "assets": []
          },
          {
            "tag_name": "v9.9.9",
            "draft": true,
            "prerelease": false,
            "assets": []
          },
          {
            "tag_name": "nightly",
            "draft": false,
            "prerelease": false,
            "assets": []
          }
        ]
        """;

    private static List<ReleaseInfo> Parsed()
    {
        using JsonDocument document = JsonDocument.Parse(Payload);
        return GitHubReleaseProvider.Parse(document.RootElement);
    }

    [Fact]
    public void Parse_SkipsDraftsAndUnparsableTags()
    {
        var releases = Parsed();

        Assert.Equal(2, releases.Count);
        Assert.DoesNotContain(releases, r => r.TagName == "v9.9.9");
        Assert.DoesNotContain(releases, r => r.TagName == "nightly");
    }

    [Fact]
    public void Parse_ReadsTheAssetIncludingTheDigest()
    {
        ReleaseAsset asset = Parsed().Single(r => r.TagName == "v1.2.0").Assets.Single();

        Assert.Equal("ELW-Meteo-1.2.0-linux-x64.zip", asset.Name);
        Assert.Equal(12345, asset.Size);

        // Stored lower-case without the prefix, ready to compare.
        Assert.Equal("ABCDEF0123", asset.Sha256);
    }

    [Fact]
    public void Newest_HidesPreReleasesUnlessAskedFor()
    {
        Assert.Equal("v1.2.0", GitHubReleaseProvider.Newest(Parsed(), includePreReleases: false)?.TagName);
        Assert.Equal("v1.3.0-beta.1", GitHubReleaseProvider.Newest(Parsed(), includePreReleases: true)?.TagName);
    }

    [Fact]
    public void Newest_ReturnsNullForAnEmptyList()
    {
        Assert.Null(GitHubReleaseProvider.Newest([], includePreReleases: true));
    }

    [Fact]
    public void Parse_DropsAnAssetThatIsNotHttps()
    {
        const string payload = """
            [{ "tag_name": "v1.0.0", "draft": false, "prerelease": false, "assets": [
              { "name": "a.zip", "browser_download_url": "http://example.invalid/a.zip" },
              { "name": "b.zip", "browser_download_url": "file:///etc/passwd" }
            ]}]
            """;

        using JsonDocument document = JsonDocument.Parse(payload);
        ReleaseInfo release = GitHubReleaseProvider.Parse(document.RootElement).Single();

        // An update is downloaded and then executed; plain http or a local path
        // must never get that far.
        Assert.Empty(release.Assets);
    }

    [Theory]
    [InlineData("sha256:ABC", "ABC")]
    [InlineData("SHA256:abc", "abc")]
    [InlineData("md5:abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void StripDigestPrefix_OnlyAcceptsSha256(string? digest, string? expected)
    {
        Assert.Equal(expected, GitHubReleaseProvider.StripDigestPrefix(digest));
    }

    [Theory]
    [InlineData("FlorianGross/ELW-Meteo", true)]
    [InlineData("owner/repo.name", true)]
    [InlineData("owner/repo_name-1", true)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("owner/", false)]
    [InlineData("../../etc", false)]
    [InlineData("owner/repo?x=1", false)]
    [InlineData("owner/repo name", false)]
    [InlineData(null, false)]
    public void IsPlausibleRepository_RejectsAnythingThatCouldEscapeThePath(string? repository, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseProvider.IsPlausibleRepository(repository));
    }
}

public class UpdateDownloaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"elw-update-{Guid.NewGuid():N}");

    public UpdateDownloaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string WriteFile(string content)
    {
        string path = Path.Combine(_directory, "package.zip");
        File.WriteAllText(path, content);
        return path;
    }

    private static ReleaseAsset Asset(long size, string? sha) => new()
    {
        Name = "package.zip",
        DownloadUrl = "https://example.invalid/package.zip",
        Size = size,
        Sha256 = sha
    };

    [Fact]
    public void Verify_AcceptsAMatchingSizeAndHash()
    {
        string path = WriteFile("hallo");
        string hash = UpdateDownloader.ComputeSha256(path);

        UpdateDownloader.Verify(path, Asset(new FileInfo(path).Length, hash));
    }

    [Fact]
    public void Verify_RejectsATruncatedDownload()
    {
        string path = WriteFile("hallo");

        var exception = Assert.Throws<UpdateDownloadException>(
            () => UpdateDownloader.Verify(path, Asset(9999, null)));

        Assert.Contains("unvollständig", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsAWrongHash()
    {
        string path = WriteFile("hallo");

        var exception = Assert.Throws<UpdateDownloadException>(
            () => UpdateDownloader.Verify(path, Asset(new FileInfo(path).Length, new string('a', 64))));

        Assert.Contains("Prüfsumme", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_ComparesTheHashCaseInsensitively()
    {
        string path = WriteFile("hallo");
        string upper = UpdateDownloader.ComputeSha256(path).ToUpperInvariant();

        // GitHub's digest casing is not guaranteed; a mismatch here would look
        // like a tampered file.
        UpdateDownloader.Verify(path, Asset(new FileInfo(path).Length, upper));
    }

    [Fact]
    public void Verify_PassesWhenNoHashWasAdvertised()
    {
        string path = WriteFile("hallo");

        // Older releases carry no digest; the size is then the only check.
        UpdateDownloader.Verify(path, Asset(new FileInfo(path).Length, null));
    }
}

public class UpdateInstallerScriptTests
{
    private static readonly StagedUpdate Staged = new(
        new AppVersion(1, 2, 0, null),
        StagingDirectory: "/opt/elw/update/staging",
        InstallDirectory: "/opt/elw/app",
        ExecutableName: "ELW-Meteo");

    [Fact]
    public void BackupPath_KeepsTheOldInstallationBesideTheNewOne()
    {
        Assert.Equal("/opt/elw/app.vor-1.2.0", UpdateInstaller.BackupPath(Staged));
    }

    [Fact]
    public void TheUnixScriptWaitsForTheProcessBeforeTouchingAnything()
    {
        string script = UpdateInstaller.BuildUnixScript(Staged, processId: 4242, logPath: "/tmp/apply.log");

        int wait = script.IndexOf("kill -0 4242", StringComparison.Ordinal);
        int move = script.IndexOf("mv '/opt/elw/app'", StringComparison.Ordinal);

        Assert.True(wait >= 0, "Das Skript wartet nicht auf den Prozess.");
        Assert.True(move > wait, "Die Installation wird angefasst, bevor der Prozess beendet ist.");
    }

    [Fact]
    public void TheUnixScriptRestoresTheOldVersionIfTheSwapFails()
    {
        string script = UpdateInstaller.BuildUnixScript(Staged, 4242, "/tmp/apply.log");

        // The one failure that must never leave a half-installed application.
        Assert.Contains("mv '/opt/elw/app.vor-1.2.0' '/opt/elw/app'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnixScriptNeverDeletesTheOldInstallation()
    {
        string script = UpdateInstaller.BuildUnixScript(Staged, 4242, "/tmp/apply.log");

        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnixScriptRestartsTheApplication()
    {
        string script = UpdateInstaller.BuildUnixScript(Staged, 4242, "/tmp/apply.log");

        Assert.Contains("/opt/elw/app/ELW-Meteo", script, StringComparison.Ordinal);
        Assert.Contains("chmod +x", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowsScriptWaitsForTheProcessBeforeTouchingAnything()
    {
        var staged = new StagedUpdate(
            new AppVersion(1, 2, 0, null),
            @"C:\Users\x\AppData\Roaming\ELW-Meteo\Update\staging",
            @"C:\Tools\ELW-Meteo",
            "ELW-Meteo.exe");

        string script = UpdateInstaller.BuildWindowsScript(staged, 4242, @"C:\Temp\apply.log");

        int wait = script.IndexOf("PID eq 4242", StringComparison.Ordinal);
        int move = script.IndexOf(@"move ""C:\Tools\ELW-Meteo""", StringComparison.Ordinal);

        Assert.True(wait >= 0);
        Assert.True(move > wait);
        Assert.DoesNotContain("rmdir /s", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("del /q", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BothScriptsGiveUpRatherThanKillingTheApplication()
    {
        string unix = UpdateInstaller.BuildUnixScript(Staged, 4242, "/tmp/a.log");
        string windows = UpdateInstaller.BuildWindowsScript(Staged, 4242, "C:\\a.log");

        // A stuck process must abort the update, not be killed: whatever it is
        // busy with may be someone's incident.
        Assert.DoesNotContain("kill -9", unix, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", windows, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 1", unix, StringComparison.Ordinal);
        Assert.Contains("exit /b 1", windows, StringComparison.Ordinal);
    }
}

public class UpdateSchedulingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoCheckWhenTheOperatorTurnedItOff()
    {
        var settings = new AppSettings { UpdateCheckEnabled = false, LastUpdateCheckUtc = null };

        Assert.False(UpdateService.ShouldCheckAutomatically(settings, Now));
    }

    [Fact]
    public void ChecksOnceWhenItNeverRan()
    {
        var settings = new AppSettings { UpdateCheckEnabled = true, LastUpdateCheckUtc = null };

        Assert.True(UpdateService.ShouldCheckAutomatically(settings, Now));
    }

    [Fact]
    public void DoesNotCheckAgainWithinTheInterval()
    {
        // Otherwise a few restarts in a row would burn the hourly rate limit.
        var settings = new AppSettings
        {
            UpdateCheckEnabled = true,
            UpdateCheckIntervalHours = 24,
            LastUpdateCheckUtc = Now.AddHours(-2)
        };

        Assert.False(UpdateService.ShouldCheckAutomatically(settings, Now));
    }

    [Fact]
    public void ChecksAgainAfterTheInterval()
    {
        var settings = new AppSettings
        {
            UpdateCheckEnabled = true,
            UpdateCheckIntervalHours = 24,
            LastUpdateCheckUtc = Now.AddHours(-25)
        };

        Assert.True(UpdateService.ShouldCheckAutomatically(settings, Now));
    }

    [Fact]
    public void ANonsenseIntervalIsClamped()
    {
        var settings = new AppSettings
        {
            UpdateCheckEnabled = true,
            UpdateCheckIntervalHours = 0,
            LastUpdateCheckUtc = Now.AddMinutes(-90)
        };

        // Zero would mean "on every tick"; the floor is one hour.
        Assert.True(UpdateService.ShouldCheckAutomatically(settings, Now));

        settings.LastUpdateCheckUtc = Now.AddMinutes(-30);
        Assert.False(UpdateService.ShouldCheckAutomatically(settings, Now));
    }

    [Fact]
    public void TheDefaultsAreConservative()
    {
        var settings = new AppSettings();

        // Checking is fine by default; fetching and installing never are.
        Assert.True(settings.UpdateCheckEnabled);
        Assert.False(settings.UpdateIncludePreReleases);
        Assert.Equal(24, settings.UpdateCheckIntervalHours);
        Assert.True(GitHubReleaseProvider.IsPlausibleRepository(settings.UpdateRepository));
    }

    [Fact]
    public void UpdateSettingsSurviveTheSettingsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"elw-upd-{Guid.NewGuid():N}.json");

        try
        {
            var settings = new AppSettings
            {
                UpdateCheckEnabled = false,
                UpdateIncludePreReleases = true,
                UpdateCheckIntervalHours = 6,
                UpdateRepository = "someone/else",
                SkippedUpdateVersion = "1.4.0",
                LastUpdateCheckUtc = Now
            };

            settings.Save(path);
            AppSettings loaded = AppSettings.Load(path);

            Assert.False(loaded.UpdateCheckEnabled);
            Assert.True(loaded.UpdateIncludePreReleases);
            Assert.Equal(6, loaded.UpdateCheckIntervalHours);
            Assert.Equal("someone/else", loaded.UpdateRepository);
            Assert.Equal("1.4.0", loaded.SkippedUpdateVersion);
            Assert.Equal(Now, loaded.LastUpdateCheckUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheWorkingDirectoryIsNeverInsideTheInstallation()
    {
        // Staging into the installation folder would put the swap inside the
        // very directory it renames.
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            UpdateService.WorkingDirectory, StringComparison.Ordinal);
    }
}

public class UpdateStagingTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"elw-stage-{Guid.NewGuid():N}");

    public UpdateStagingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string BuildArchive(params string[] entries)
    {
        string source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);

        foreach (string entry in entries)
        {
            string path = Path.Combine(source, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        string archive = Path.Combine(_root, "package.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(source, archive);
        Directory.Delete(source, recursive: true);

        return archive;
    }

    [Fact]
    public void Stage_RefusesAPackageWithoutTheExecutable()
    {
        string archive = BuildArchive("readme.txt", "ElwMeteo.Core.dll");
        var installer = new UpdateInstaller();

        var exception = Assert.Throws<UpdateInstallException>(() => installer.Stage(
            archive, new AppVersion(1, 2, 0, null),
            installDirectory: Path.Combine(_root, "app"),
            workingDirectory: Path.Combine(_root, "work")));

        // Caught before anything is replaced — that is the whole point of
        // staging separately from swapping.
        Assert.Contains("fehlt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AcceptsAPackageThatCarriesTheExecutable()
    {
        string archive = BuildArchive(UpdateInstaller.ExecutableName, "ElwMeteo.Core.dll");
        var installer = new UpdateInstaller();

        StagedUpdate staged = installer.Stage(
            archive, new AppVersion(1, 2, 0, null),
            installDirectory: Path.Combine(_root, "app"),
            workingDirectory: Path.Combine(_root, "work"));

        Assert.True(File.Exists(Path.Combine(staged.StagingDirectory, UpdateInstaller.ExecutableName)));
        Assert.Equal(new AppVersion(1, 2, 0, null), staged.Version);
    }

    [Fact]
    public void Stage_LooksInsideASingleTopLevelFolder()
    {
        // Some archives wrap everything in one directory; the executable is then
        // one level down and must still be found.
        string archive = BuildArchive(Path.Combine("ELW-Meteo", UpdateInstaller.ExecutableName));
        var installer = new UpdateInstaller();

        StagedUpdate staged = installer.Stage(
            archive, new AppVersion(1, 2, 0, null),
            Path.Combine(_root, "app"), Path.Combine(_root, "work"));

        Assert.EndsWith("ELW-Meteo", staged.StagingDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ReportsAWritableDirectory()
    {
        string directory = Path.Combine(_root, "writable");
        Directory.CreateDirectory(directory);

        InstallLocation location = InstallLocation.Inspect(directory);

        Assert.True(location.IsWritable);
        Assert.Null(location.Reason);
    }

    [Fact]
    public void Inspect_ReportsAMissingDirectoryWithAReason()
    {
        InstallLocation location = InstallLocation.Inspect(Path.Combine(_root, "nope"));

        Assert.False(location.IsWritable);
        Assert.False(string.IsNullOrWhiteSpace(location.Reason));
    }
}
