using System.Globalization;
using System.Text.RegularExpressions;

namespace ElwMeteo.Core.Services;

/// <summary>
/// The TIME dimension a WMS layer advertises in its capabilities document.
///
/// This is what turns a static overlay into an animated one: the server names
/// the instants it can render, and each GetMap request picks one via TIME=.
/// The DWD publishes its radar composite plus the two-hour extrapolation this
/// way, in five-minute steps — finer than any tile service and, unlike them,
/// the official product.
/// </summary>
public sealed partial record WmsTimeDimension(IReadOnlyList<DateTimeOffset> Instants, string? DefaultValue)
{
    public static WmsTimeDimension Empty { get; } = new([], null);

    public bool IsEmpty => Instants.Count == 0;

    /// <summary>Instants split at "now" into observation and forecast.</summary>
    public IEnumerable<DateTimeOffset> Observed(DateTimeOffset now) => Instants.Where(i => i <= now);

    public IEnumerable<DateTimeOffset> Forecast(DateTimeOffset now) => Instants.Where(i => i > now);

    /// <summary>WMS wants the instant as ISO 8601 in UTC, to the second.</summary>
    public static string Format(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the value of a time dimension. Servers use two forms, and GeoServer
    /// emits both depending on the layer:
    ///   • an explicit comma-separated list of instants, and
    ///   • one or more <c>start/end/period</c> intervals in ISO 8601.
    /// Mixed content is accepted, since a layer may advertise both.
    /// </summary>
    /// <param name="maxInstants">Guard against a server advertising a decade of five-minute steps.</param>
    public static WmsTimeDimension Parse(string? value, string? defaultValue = null, int maxInstants = 512)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty;
        }

        var instants = new List<DateTimeOffset>();

        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('/'))
            {
                instants.AddRange(ExpandInterval(part, maxInstants - instants.Count));
            }
            else if (TryParseInstant(part, out DateTimeOffset instant))
            {
                instants.Add(instant);
            }

            if (instants.Count >= maxInstants)
            {
                break;
            }
        }

        if (instants.Count == 0)
        {
            return Empty;
        }

        return new WmsTimeDimension(
            instants.Distinct().OrderBy(i => i).Take(maxInstants).ToList(),
            defaultValue);
    }

    /// <summary>Expands a <c>start/end/period</c> interval into concrete instants.</summary>
    private static IEnumerable<DateTimeOffset> ExpandInterval(string interval, int limit)
    {
        string[] pieces = interval.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pieces.Length < 2 ||
            !TryParseInstant(pieces[0], out DateTimeOffset start) ||
            !TryParseInstant(pieces[1], out DateTimeOffset end))
        {
            yield break;
        }

        // A two-part interval has no step; the endpoints are all we can offer.
        TimeSpan? period = pieces.Length >= 3 ? ParsePeriod(pieces[2]) : null;

        if (period is not { } step || step <= TimeSpan.Zero)
        {
            yield return start;

            if (end != start)
            {
                yield return end;
            }

            yield break;
        }

        int emitted = 0;
        for (DateTimeOffset instant = start; instant <= end && emitted < limit; instant += step)
        {
            yield return instant;
            emitted++;
        }
    }

    /// <summary>
    /// ISO 8601 duration, restricted to the parts a WMS time step actually uses.
    /// Months and years are deliberately unsupported: they are not fixed-length,
    /// and no radar product needs them.
    /// </summary>
    internal static TimeSpan? ParsePeriod(string period)
    {
        Match match = PeriodPattern().Match(period.Trim());
        if (!match.Success)
        {
            return null;
        }

        double Part(string group) =>
            match.Groups[group].Success
                ? double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)
                : 0;

        TimeSpan result =
            TimeSpan.FromDays(Part("days")) +
            TimeSpan.FromHours(Part("hours")) +
            TimeSpan.FromMinutes(Part("minutes")) +
            TimeSpan.FromSeconds(Part("seconds"));

        return result > TimeSpan.Zero ? result : null;
    }

    private static bool TryParseInstant(string value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out instant);

    [GeneratedRegex(
        @"^P(?:(?<days>\d+(?:\.\d+)?)D)?(?:T(?:(?<hours>\d+(?:\.\d+)?)H)?(?:(?<minutes>\d+(?:\.\d+)?)M)?(?:(?<seconds>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();
}
