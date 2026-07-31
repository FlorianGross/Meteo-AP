using System.Runtime.InteropServices;

namespace ElwMeteo.Core.Updates;

/// <summary>One downloadable file attached to a release.</summary>
public sealed record ReleaseAsset
{
    public required string Name { get; init; }

    public required string DownloadUrl { get; init; }

    public long Size { get; init; }

    /// <summary>
    /// The SHA-256 GitHub reports for the asset, without the "sha256:" prefix,
    /// or null when the API did not supply one.
    /// </summary>
    public string? Sha256 { get; init; }
}

/// <summary>A release as the GitHub API describes it.</summary>
public sealed record ReleaseInfo
{
    public required AppVersion Version { get; init; }

    public required string TagName { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; init; }

    public bool IsPreRelease { get; init; }

    public string HtmlUrl { get; init; } = string.Empty;

    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];
}

/// <summary>Which platform a package is for.</summary>
public enum UpdatePlatform
{
    WindowsX64,
    LinuxX64,
    MacOsX64,
    MacOsArm64,
    Unknown
}

/// <summary>
/// Picks the asset that fits the machine the application is running on.
///
/// The release carries five archives; installing the wrong one would leave a
/// folder full of binaries for another processor. The names follow the release
/// workflow, so this and the workflow have to agree — a test pins the patterns.
/// </summary>
public static class UpdatePackageSelector
{
    /// <summary>What the current process is running on.</summary>
    public static UpdatePlatform CurrentPlatform =>
        Detect(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

    internal static UpdatePlatform Detect(
        string osDescription, Architecture architecture,
        bool isWindows, bool isLinux, bool isMacOs)
    {
        _ = osDescription;

        if (isWindows)
        {
            return architecture is Architecture.X64 or Architecture.X86
                ? UpdatePlatform.WindowsX64
                : UpdatePlatform.Unknown;
        }

        if (isLinux)
        {
            return architecture == Architecture.X64 ? UpdatePlatform.LinuxX64 : UpdatePlatform.Unknown;
        }

        if (isMacOs)
        {
            return architecture switch
            {
                Architecture.Arm64 => UpdatePlatform.MacOsArm64,
                Architecture.X64 => UpdatePlatform.MacOsX64,
                _ => UpdatePlatform.Unknown
            };
        }

        return UpdatePlatform.Unknown;
    }

    /// <summary>
    /// The name fragment identifying a package for a platform. The standalone
    /// Windows archive is preferred over the framework-dependent one: an update
    /// must not depend on a runtime the machine may not have.
    /// </summary>
    internal static IReadOnlyList<string> Patterns(UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.WindowsX64 => ["win-x64-standalone", "win-x64"],
        UpdatePlatform.LinuxX64 => ["linux-x64"],
        UpdatePlatform.MacOsArm64 => ["macos-arm64"],
        UpdatePlatform.MacOsX64 => ["macos-x64"],
        _ => []
    };

    /// <summary>Returns the best asset for the platform, or null if none fits.</summary>
    public static ReleaseAsset? Select(ReleaseInfo release, UpdatePlatform platform)
    {
        foreach (string pattern in Patterns(platform))
        {
            ReleaseAsset? match = release.Assets.FirstOrDefault(a =>
                a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public static string Describe(UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.WindowsX64 => "Windows (x64)",
        UpdatePlatform.LinuxX64 => "Linux (x64)",
        UpdatePlatform.MacOsX64 => "macOS (Intel)",
        UpdatePlatform.MacOsArm64 => "macOS (Apple Silicon)",
        _ => "unbekannte Plattform"
    };
}
