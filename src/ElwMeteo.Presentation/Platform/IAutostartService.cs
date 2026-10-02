using System.Diagnostics;
using System.Runtime.Versioning;
using ElwMeteo.Core.Kiosk;

namespace ElwMeteo.Presentation.Platform;

/// <summary>
/// Registers the application to start with the user's session, and says whether
/// it currently is.
///
/// Per user, never machine-wide. The installer is a per-user MSI and the
/// executable lives under the operator's own profile, so a machine-wide entry
/// would point at a path that a second account cannot even read. It also means
/// none of this needs administrator rights, which matters: a crew cannot be
/// expected to find somebody with the local administrator password before the
/// vehicle screen comes up by itself.
/// </summary>
public interface IAutostartService
{
    /// <summary>True when this build can register autostart on the running system.</summary>
    bool IsSupported { get; }

    /// <summary>Where the entry goes, for the settings page to show.</summary>
    string Describe();

    /// <summary>Whether an entry is present right now.</summary>
    bool IsEnabled();

    /// <summary>
    /// Adds or removes the entry. Returns false with a reason rather than
    /// throwing: a locked registry or a read-only home directory is a thing to
    /// report in the settings page, not a reason to take the application down.
    /// </summary>
    bool TrySet(bool enabled, out string? error);
}

/// <summary>
/// Autostart through whatever the running system uses for it: the per-user Run
/// key on Windows, a freedesktop autostart file on Linux, a LaunchAgent on
/// macOS. The content of all three comes from
/// <see cref="AutostartEntry"/> so the quoting can be tested; what is left here
/// is the writing.
/// </summary>
public sealed class SystemAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _executablePath;

    /// <param name="executablePath">
    /// Path to launch. Null resolves the running process, which is what the
    /// composition roots pass; the tests pass a path so they need no installed
    /// application.
    /// </param>
    public SystemAutostartService(string? executablePath = null)
    {
        _executablePath = executablePath ?? ResolveExecutablePath();
    }

    public bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public string Describe()
    {
        if (OperatingSystem.IsWindows())
        {
            return $@"HKCU\{RunKeyPath}\{AutostartEntry.Name}";
        }

        if (OperatingSystem.IsLinux())
        {
            return Path.Combine(LinuxAutostartDirectory(), AutostartEntry.DesktopFileName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(MacLaunchAgentDirectory(), AutostartEntry.LaunchAgentFileName);
        }

        return "Auf diesem System nicht unterstützt.";
    }

    public bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return ReadRunValue() is not null;
            }

            return IsSupported && File.Exists(Describe());
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    public bool TrySet(bool enabled, out string? error)
    {
        error = null;

        if (!IsSupported)
        {
            error = "Automatischer Start ist auf diesem System nicht umgesetzt.";
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                WriteRunValue(enabled);
                return true;
            }

            string path = Describe();

            if (!enabled)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string content = OperatingSystem.IsLinux()
                ? AutostartEntry.BuildDesktopEntry(_executablePath)
                : AutostartEntry.BuildLaunchAgent(_executablePath);

            File.WriteAllText(path, content);

            if (OperatingSystem.IsLinux())
            {
                MakeExecutable(path);
            }

            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    // ------------------------------------------------------------- Windows

    [SupportedOSPlatform("windows")]
    private static string? ReadRunValue()
    {
        using Microsoft.Win32.RegistryKey? key =
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

        return key?.GetValue(AutostartEntry.Name) as string;
    }

    [SupportedOSPlatform("windows")]
    private void WriteRunValue(bool enabled)
    {
        using Microsoft.Win32.RegistryKey key =
            Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(
                AutostartEntry.Name,
                AutostartEntry.BuildWindowsCommand(_executablePath),
                Microsoft.Win32.RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutostartEntry.Name, throwOnMissingValue: false);
        }
    }

    // ------------------------------------------------------- unix helpers

    private static string LinuxAutostartDirectory()
    {
        // XDG_CONFIG_HOME when the session sets it, ~/.config otherwise.
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        return string.IsNullOrWhiteSpace(configHome)
            ? Path.Combine(HomeDirectory(), ".config", "autostart")
            : Path.Combine(configHome, "autostart");
    }

    private static string MacLaunchAgentDirectory() =>
        Path.Combine(HomeDirectory(), "Library", "LaunchAgents");

    private static string HomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrEmpty(home)
            ? Environment.GetEnvironmentVariable("HOME") ?? "."
            : home;
    }

    /// <summary>
    /// Marks the autostart file executable. Not every desktop needs it, GNOME
    /// among them, but KDE will refuse a file without the bit set — and a
    /// silently ignored autostart entry is the hardest kind to diagnose.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private static void MakeExecutable(string path)
    {
        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // A filesystem without permission bits, e.g. a FAT stick. The entry
            // is written either way; losing the bit is not worth failing over.
        }
    }

    /// <summary>
    /// The running application's launcher. <c>ProcessPath</c> is the actual
    /// executable, which for a published single-file or apphost build is the
    /// <c>.exe</c> — unlike the entry assembly, which is the managed
    /// <c>.dll</c> and cannot be started by the shell.
    /// </summary>
    private static string ResolveExecutablePath()
    {
        string? path = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        using Process current = Process.GetCurrentProcess();
        return current.MainModule?.FileName ?? string.Empty;
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or
              System.Security.SecurityException or PlatformNotSupportedException or
              ArgumentException or NotSupportedException;
}

/// <summary>
/// Stands in where autostart cannot be offered, so the settings view model needs
/// no null checks.
/// </summary>
public sealed class UnsupportedAutostartService(string reason) : IAutostartService
{
    public bool IsSupported => false;

    public string Describe() => reason;

    public bool IsEnabled() => false;

    public bool TrySet(bool enabled, out string? error)
    {
        error = reason;
        return false;
    }
}
