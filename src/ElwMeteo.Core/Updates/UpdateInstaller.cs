using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ElwMeteo.Core.Updates;

public sealed class UpdateInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Where an update would be written, and whether that is possible.</summary>
public sealed record InstallLocation(string Directory, bool IsWritable, string? Reason)
{
    public static InstallLocation Inspect(string directory)
    {
        try
        {
            if (!System.IO.Directory.Exists(directory))
            {
                return new InstallLocation(directory, false, "Das Installationsverzeichnis existiert nicht.");
            }

            // Actually write, rather than reasoning about permissions: on Windows
            // a folder under Program Files looks writable until it is not.
            string probe = Path.Combine(directory, $".elw-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);

            // The parent is needed too — the swap renames the directory itself.
            string? parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));

            if (parent is not null && System.IO.Directory.Exists(parent))
            {
                string parentProbe = Path.Combine(parent, $".elw-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(parentProbe, "x");
                File.Delete(parentProbe);
            }

            return new InstallLocation(directory, true, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new InstallLocation(directory, false,
                "Das Installationsverzeichnis ist schreibgeschützt — typisch für einen Ordner unter " +
                "„Programme“. Die Anwendung müsste an einen Ort verschoben werden, an dem der " +
                "angemeldete Benutzer schreiben darf, oder das Paket manuell entpackt werden.");
        }
        catch (Exception ex) when (ex is IOException)
        {
            return new InstallLocation(directory, false,
                $"Das Installationsverzeichnis ist nicht beschreibbar: {ex.Message}");
        }
    }
}

/// <summary>An update that has been unpacked and checked, waiting for a restart.</summary>
public sealed record StagedUpdate(
    AppVersion Version,
    string StagingDirectory,
    string InstallDirectory,
    string ExecutableName);

/// <summary>
/// Unpacks an update beside the installation and swaps it in on restart.
///
/// A running program cannot replace its own files — on Windows the executable is
/// locked outright. So the work is split: everything that can fail safely happens
/// while the application is still running and still working, and only a directory
/// rename happens afterwards, from a small script that waits for the process to
/// exit.
///
/// The order matters and is deliberate:
///   1. unpack into a staging folder and verify the executable is in there
///   2. rename the installation aside, keeping it complete
///   3. move staging into place
///   4. on any failure in 3, move the old installation back
/// A half-copied installation is the one outcome worth going to lengths to avoid:
/// mixed assemblies from two versions fail at load time, which looks like a
/// broken machine rather than a failed update.
/// </summary>
public sealed class UpdateInstaller
{
    /// <summary>Name of the executable, used to check that staging is complete.</summary>
    public static string ExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ELW-Meteo.exe" : "ELW-Meteo";

    /// <summary>
    /// Unpacks the archive into a staging folder next to the installation.
    /// Nothing in the installation is touched.
    /// </summary>
    public StagedUpdate Stage(
        string archivePath,
        AppVersion version,
        string installDirectory,
        string workingDirectory)
    {
        string staging = Path.Combine(workingDirectory, "staging");

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new UpdateInstallException($"Das Paket konnte nicht entpackt werden: {ex.Message}", ex);
        }

        // Some archives carry a single top-level folder; use it as the root then.
        string root = staging;
        string[] entries = Directory.GetFileSystemEntries(staging);

        if (entries.Length == 1 && Directory.Exists(entries[0]))
        {
            root = entries[0];
        }

        string executable = ExecutableName;

        if (!File.Exists(Path.Combine(root, executable)))
        {
            throw new UpdateInstallException(
                $"Im Paket fehlt „{executable}“. Das Paket passt nicht zu dieser Plattform — " +
                "es wird nichts ersetzt.");
        }

        return new StagedUpdate(version, root, installDirectory, executable);
    }

    /// <summary>
    /// Writes the swap script and starts it. The caller must shut the
    /// application down immediately afterwards; until it exits the script waits
    /// and the installation stays untouched.
    /// </summary>
    public void Apply(StagedUpdate staged, string workingDirectory, int processId)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string scriptPath = Path.Combine(workingDirectory, isWindows ? "apply-update.cmd" : "apply-update.sh");
        string logPath = Path.Combine(workingDirectory, "apply-update.log");

        string script = isWindows
            ? BuildWindowsScript(staged, processId, logPath)
            : BuildUnixScript(staged, processId, logPath);

        try
        {
            File.WriteAllText(scriptPath, script);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var start = isWindows
                ? new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                : new ProcessStartInfo("/bin/sh", $"\"{scriptPath}\"");

            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = workingDirectory;

            Process.Start(start);
        }
        catch (Exception ex)
        {
            throw new UpdateInstallException(
                $"Der Austauschvorgang konnte nicht gestartet werden: {ex.Message}", ex);
        }
    }

    // ------------------------------------------------------------- scripts
    //
    // Generated as text so they can be asserted on in a test. Neither script
    // deletes anything before the new version is confirmed in place; the old
    // installation is kept as a sibling folder so a failed swap is recoverable
    // by hand as well.

    internal static string BuildWindowsScript(StagedUpdate staged, int processId, string logPath)
    {
        string backup = BackupPath(staged);

        return $"""
            @echo off
            setlocal
            set "LOG={logPath}"
            echo [%DATE% %TIME%] Aktualisierung auf {staged.Version} gestartet>>"%LOG%"

            rem Auf das Ende des laufenden Prozesses warten, hoechstens 60 Sekunden.
            set /a TRIES=0
            :wait
            tasklist /FI "PID eq {processId}" 2>nul | find "{processId}" >nul
            if errorlevel 1 goto ready
            set /a TRIES+=1
            if %TRIES% GEQ 60 (
              echo [%DATE% %TIME%] Prozess {processId} laeuft noch - abgebrochen>>"%LOG%"
              exit /b 1
            )
            timeout /t 1 /nobreak >nul
            goto wait

            :ready
            rem Alte Installation zur Seite legen, nicht loeschen.
            move "{staged.InstallDirectory}" "{backup}" >>"%LOG%" 2>&1
            if errorlevel 1 (
              echo [%DATE% %TIME%] Umbenennen fehlgeschlagen - nichts geaendert>>"%LOG%"
              exit /b 1
            )

            move "{staged.StagingDirectory}" "{staged.InstallDirectory}" >>"%LOG%" 2>&1
            if errorlevel 1 (
              echo [%DATE% %TIME%] Einsetzen fehlgeschlagen - alte Version wird zurueckgeholt>>"%LOG%"
              move "{backup}" "{staged.InstallDirectory}" >>"%LOG%" 2>&1
              exit /b 1
            )

            echo [%DATE% %TIME%] Fertig - alte Version liegt in {backup}>>"%LOG%"
            start "" "{Path.Combine(staged.InstallDirectory, staged.ExecutableName)}"
            endlocal
            """;
    }

    internal static string BuildUnixScript(StagedUpdate staged, int processId, string logPath)
    {
        string backup = BackupPath(staged);
        string executable = Path.Combine(staged.InstallDirectory, staged.ExecutableName);

        return $"""
            #!/bin/sh
            set -u
            LOG='{logPath}'
            echo "[$(date '+%Y-%m-%d %H:%M:%S')] Aktualisierung auf {staged.Version} gestartet" >>"$LOG"

            # Auf das Ende des laufenden Prozesses warten, hoechstens 60 Sekunden.
            tries=0
            while kill -0 {processId} 2>/dev/null; do
              tries=$((tries + 1))
              if [ "$tries" -ge 60 ]; then
                echo "[$(date '+%Y-%m-%d %H:%M:%S')] Prozess {processId} laeuft noch - abgebrochen" >>"$LOG"
                exit 1
              fi
              sleep 1
            done

            # Alte Installation zur Seite legen, nicht loeschen.
            if ! mv '{staged.InstallDirectory}' '{backup}' >>"$LOG" 2>&1; then
              echo "[$(date '+%Y-%m-%d %H:%M:%S')] Umbenennen fehlgeschlagen - nichts geaendert" >>"$LOG"
              exit 1
            fi

            if ! mv '{staged.StagingDirectory}' '{staged.InstallDirectory}' >>"$LOG" 2>&1; then
              echo "[$(date '+%Y-%m-%d %H:%M:%S')] Einsetzen fehlgeschlagen - alte Version wird zurueckgeholt" >>"$LOG"
              mv '{backup}' '{staged.InstallDirectory}' >>"$LOG" 2>&1
              exit 1
            fi

            chmod +x '{executable}' 2>/dev/null || true
            echo "[$(date '+%Y-%m-%d %H:%M:%S')] Fertig - alte Version liegt in {backup}" >>"$LOG"

            # Abgelöst starten, damit die neue Instanz dieses Skript ueberlebt.
            ('{executable}' >/dev/null 2>&1 &)
            """;
    }

    /// <summary>Where the previous installation is kept after a successful swap.</summary>
    internal static string BackupPath(StagedUpdate staged)
    {
        string trimmed = staged.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return $"{trimmed}.vor-{staged.Version}";
    }
}
