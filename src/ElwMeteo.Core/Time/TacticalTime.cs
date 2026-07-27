namespace ElwMeteo.Core.Time;

/// <summary>
/// Formats a point in time as a NATO/BOS "Date Time Group" (DTG) — in German
/// fire and rescue services commonly called the <em>taktische Uhrzeit</em>.
///
/// Layout: <c>DDHHMMZmmmYY</c>, e.g. <c>271055ZJUL26</c>
///   DD   day of month
///   HHMM 24 h clock
///   Z    military time-zone letter (Z = UTC, A = UTC+1, B = UTC+2, ...)
///   mmm  three-letter English month, upper case
///   YY   two-digit year
/// </summary>
public static class TacticalTime
{
    private static readonly string[] MonthAbbreviations =
    [
        "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
        "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
    ];

    /// <summary>
    /// Military time-zone letters ordered by whole-hour offset from UTC.
    /// Index 0 == UTC. "J" is deliberately absent from the sequence because it
    /// is reserved for "local time of the observer".
    /// </summary>
    private const string EastLetters = "ABCDEFGHIKLM";  // UTC+1 .. UTC+12
    private const string WestLetters = "NOPQRSTUVWXY";  // UTC-1 .. UTC-12

    /// <summary>Zone letter for an offset. Half-hour offsets have no letter, so those fall back to "J" (local).</summary>
    public static char ZoneLetter(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return 'Z';
        }

        // Only whole-hour offsets map onto a military zone letter.
        if (offset.Ticks % TimeSpan.TicksPerHour != 0)
        {
            return 'J';
        }

        int hours = (int)offset.TotalHours;
        return hours switch
        {
            >= 1 and <= 12 => EastLetters[hours - 1],
            <= -1 and >= -12 => WestLetters[-hours - 1],
            _ => 'J'
        };
    }

    /// <summary>DTG for an instant expressed in a specific offset, e.g. 271255BJUL26.</summary>
    public static string Format(DateTimeOffset instant)
    {
        char zone = ZoneLetter(instant.Offset);
        return string.Concat(
            instant.Day.ToString("00"),
            instant.Hour.ToString("00"),
            instant.Minute.ToString("00"),
            zone.ToString(),
            MonthAbbreviations[instant.Month - 1],
            (instant.Year % 100).ToString("00"));
    }

    /// <summary>DTG in UTC ("Zulu"), e.g. 271055ZJUL26.</summary>
    public static string FormatZulu(DateTimeOffset instant) => Format(instant.ToUniversalTime());

    /// <summary>DTG in the given time zone; defaults to the machine's local zone.</summary>
    public static string FormatLocal(DateTimeOffset instant, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        return Format(TimeZoneInfo.ConvertTime(instant, zone));
    }

    /// <summary>
    /// Short form without the zone letter and month/year, as often spoken over
    /// radio when the date is already established: <c>27 / 10:55</c>.
    /// </summary>
    public static string FormatShort(DateTimeOffset instant) =>
        $"{instant.Day:00} / {instant.Hour:00}:{instant.Minute:00}";
}
