namespace ElwMeteo.Core.Configuration;

/// <summary>
/// Writes <see cref="AppSettings"/> to disk, but not on every keystroke.
///
/// Almost everything the operator adjusts is a slider or a checkbox, and a
/// slider drag produces dozens of changes per second. Writing the file each time
/// would mean dozens of writes for one gesture. So a change only marks the
/// settings dirty; a caller flushes on a timer and once more on shutdown.
///
/// The alternative — saving only when someone remembers to press a button — is
/// what this replaces: the map's layer choice, radar source and zoom limits were
/// set on the object and then lost on the next start.
/// </summary>
public sealed class SettingsStore(AppSettings settings, string? path = null)
{
    private readonly object _gate = new();
    private bool _isDirty;

    public AppSettings Settings { get; } = settings;

    /// <summary>Reason the last write failed, or null. A full disk must not crash the app.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised when a write fails, so a view can say so instead of failing silently.</summary>
    public event Action<string>? SaveFailed;

    public bool IsDirty
    {
        get
        {
            lock (_gate)
            {
                return _isDirty;
            }
        }
    }

    /// <summary>Notes that something changed. Cheap enough to call from a slider.</summary>
    public void RequestSave()
    {
        lock (_gate)
        {
            _isDirty = true;
        }
    }

    /// <summary>
    /// Writes if anything is pending. Returns true when a write actually
    /// happened, so a caller can tell "nothing to do" from "saved".
    /// </summary>
    public bool Flush()
    {
        lock (_gate)
        {
            if (!_isDirty)
            {
                return false;
            }

            // Cleared before the write: a failure is reported through LastError,
            // and retrying the same broken write every tick would only fill the
            // log. The next change marks it dirty again.
            _isDirty = false;
        }

        try
        {
            Settings.Save(path);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            SaveFailed?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>Marks dirty and writes at once — for a deliberate "Übernehmen".</summary>
    public bool SaveNow()
    {
        RequestSave();
        return Flush();
    }
}
