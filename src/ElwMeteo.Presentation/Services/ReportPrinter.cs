using System.Globalization;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.Services;

/// <summary>
/// Writes a report to disk and hands it to the system browser, where the print
/// dialogue is one keystroke away and „Save as PDF" is a destination in it.
///
/// This is the whole reason the report is HTML. There is no print dialogue to
/// implement, no PDF library to license and ship, and the result is identical
/// on Windows, macOS and Linux. It also leaves the file behind, which turns out
/// to matter more than the printing: the report can be attached to the
/// operations log or mailed on without producing it a second time.
/// </summary>
public sealed class ReportPrinter(IShellLauncher shell)
{
    /// <summary>
    /// How many reports are kept. Enough that a shift's worth survives, few
    /// enough that the folder does not quietly fill a small vehicle disk.
    /// </summary>
    private const int KeepFiles = 40;

    public string Directory { get; init; } =
        Path.Combine(AppSettings.DefaultDirectory, "Berichte");

    /// <summary>Path of the report written last, for the „open folder" button.</summary>
    public string? LastPath { get; private set; }

    /// <summary>
    /// Writes the page and opens it. Returns false with a reason rather than
    /// throwing: a report that will not print is a nuisance, not a failure worth
    /// taking a panel down for.
    /// </summary>
    public bool Produce(string html, DateTimeOffset now, out string message)
    {
        string path;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            path = Path.Combine(
                Directory,
                $"Wetterbericht-{now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.html");

            File.WriteAllText(path, html);
            LastPath = path;

            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"Bericht konnte nicht geschrieben werden: {ex.Message}";
            return false;
        }

        if (!shell.TryOpen(path, out string? error))
        {
            // The file is on disk either way, so say where — that is still a
            // usable outcome without a browser.
            message = $"Bericht liegt unter {path}, ließ sich aber nicht öffnen: {error}";
            return false;
        }

        message = "Bericht im Browser geöffnet — dort mit Strg+P drucken oder als PDF sichern.";
        return true;
    }

    /// <summary>Opens the folder the reports are written to.</summary>
    public bool OpenDirectory(out string? error)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }

        return shell.TryOpen(Directory, out error);
    }

    /// <summary>Deletes all but the most recent reports. Failure here is ignored on purpose.</summary>
    private void Prune()
    {
        try
        {
            var files = new DirectoryInfo(Directory)
                .GetFiles("Wetterbericht-*.html")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(KeepFiles)
                .ToList();

            foreach (FileInfo file in files)
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Housekeeping. Not worth reporting, never worth failing the report for.
        }
    }
}
