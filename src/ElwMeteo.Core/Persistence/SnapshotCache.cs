using System.Text.Json;
using System.Text.Json.Serialization;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Persistence;

/// <summary>
/// The last picture that was successfully retrieved, kept so a start without
/// network shows something rather than nothing.
/// </summary>
public sealed record CachedState
{
    public required WeatherSnapshot Snapshot { get; init; }

    public IReadOnlyList<DwdWarning> Warnings { get; init; } = [];

    /// <summary>Reverse-geocoded address, so the header is not blank either.</summary>
    public string? AddressLine { get; init; }

    /// <summary>Which route produced the warnings, repeated in the offline banner.</summary>
    public string? WarningSource { get; init; }

    public required DateTimeOffset SavedAtUtc { get; init; }

    public TimeSpan AgeAt(DateTimeOffset now) => now.ToUniversalTime() - SavedAtUtc;
}

/// <summary>
/// Writes the last good state to disk and reads it back on the next start.
///
/// The point is a specific morning: the vehicle is parked somewhere with no
/// usable cellular link, the application comes up, and the operator gets
/// „Abruf fehlgeschlagen" and an empty panel. A wind direction from forty
/// minutes ago is worth having for a first assessment — provided it is
/// unmistakably labelled as old, which is why <see cref="MaximumAge"/> exists
/// and why the age is carried in the record rather than inferred at the edge.
///
/// Nothing here throws. A cache that cannot be read is a cache that is not
/// used; it must never be the reason the application fails to start.
/// </summary>
public sealed class SnapshotCache(string? path = null)
{
    /// <summary>
    /// Past this the stored picture is dropped instead of shown. Twelve hours
    /// covers a shift and an overnight stand; beyond it a wind direction is not
    /// merely old but actively misleading, and offering it would be worse than
    /// the empty panel this whole feature exists to avoid.
    /// </summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path = path ?? DefaultPath;

    public static string DefaultPath =>
        Path.Combine(Configuration.AppSettings.DefaultDirectory, "last-state.json");

    /// <summary>Why the last read or write did not work, for the diagnostics tab.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Stores the state. Written to a temporary file and moved into place, so a
    /// power cut mid-write leaves the previous file intact rather than a
    /// truncated one — a corrupt cache would defeat the entire purpose.
    /// </summary>
    public bool Save(CachedState state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, SerializerOptions));
            File.Move(temporary, _path, overwrite: true);

            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reads the state back, or null when there is none, it cannot be read, or
    /// it is older than <see cref="MaximumAge"/>.
    /// </summary>
    public CachedState? Load(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<CachedState>(File.ReadAllText(_path), SerializerOptions);

            if (state is null)
            {
                return null;
            }

            // A clock that jumped backwards would otherwise produce a negative
            // age and a state that never expires.
            TimeSpan age = state.AgeAt(now);

            if (age > MaximumAge || age < TimeSpan.Zero)
            {
                return null;
            }

            LastError = null;
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>Wording for the offline banner, e.g. „vor 41 min".</summary>
    public static string DescribeAge(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "gerade eben",
        < 60 => $"vor {(int)age.TotalMinutes} min",
        _ => $"vor {(int)age.TotalHours} h {age.Minutes:00} min"
    };
}
