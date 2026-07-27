namespace ElwMeteo.Core.Meteorology;

/// <summary>Which way the wind turns.</summary>
public enum TurnDirection
{
    /// <summary>Clockwise, e.g. south-west to west — "rechtsdrehend".</summary>
    Veering,
    /// <summary>Anticlockwise, e.g. west to south-west — "linksdrehend".</summary>
    Backing
}

/// <summary>A forecast change of wind direction large enough to matter on scene.</summary>
public sealed record WindShift(
    DateTimeOffset Time,
    double FromDeg,
    double ToDeg,
    double SignedDeltaDeg,
    TurnDirection Direction)
{
    public double AbsoluteDeltaDeg => Math.Abs(SignedDeltaDeg);

    public string DirectionLabel => Direction == TurnDirection.Veering ? "rechtsdrehend" : "linksdrehend";

    /// <summary>New direction the plume will travel once the shift has happened.</summary>
    public double NewDownwindDeg => WindScale.DownwindDirection(ToDeg);

    public string Describe(DateTimeOffset now)
    {
        int minutes = (int)Math.Round((Time - now).TotalMinutes);
        string when = minutes < 90 ? $"in ca. {minutes} min" : $"in ca. {minutes / 60.0:F0} h";

        return $"Wind dreht {when} {DirectionLabel} von {WindScale.CompassPoint(FromDeg)} " +
               $"nach {WindScale.CompassPoint(ToDeg)} ({AbsoluteDeltaDeg:F0}°). " +
               $"Ausbreitung dann nach {WindScale.CompassPoint(NewDownwindDeg)}.";
    }
}

/// <summary>The strongest gust expected within the look-ahead window.</summary>
public sealed record GustPeak(DateTimeOffset Time, double GustMs)
{
    public string Describe(DateTimeOffset now)
    {
        int minutes = (int)Math.Round((Time - now).TotalMinutes);
        string when = minutes <= 0 ? "jetzt" : minutes < 90 ? $"in ca. {minutes} min" : $"gegen {Time.ToLocalTime():HH:mm} Uhr";

        return $"Böenspitze {WindScale.MsToKmh(GustMs):F0} km/h {when}.";
    }
}

/// <summary>
/// Finds the point in a wind forecast where the direction turns far enough to
/// move a smoke or gas plume onto a different set of streets.
///
/// This is the question that decides whether an evacuation order still holds an
/// hour from now, so it deserves to be answered explicitly rather than left for
/// someone to spot in an hourly table.
/// </summary>
public static class WindShiftDetector
{
    /// <summary>Default turn that counts as operationally relevant: one 45° compass sector.</summary>
    public const double DefaultThresholdDeg = 45.0;

    /// <summary>
    /// Signed difference between two bearings, in (-180, 180].
    /// Positive means clockwise (veering).
    /// </summary>
    public static double SignedDifference(double fromDeg, double toDeg)
    {
        double delta = (toDeg - fromDeg) % 360.0;

        if (delta > 180.0)
        {
            delta -= 360.0;
        }
        else if (delta <= -180.0)
        {
            delta += 360.0;
        }

        return delta;
    }

    /// <summary>
    /// First forecast step within <paramref name="horizon"/> whose direction
    /// differs from <paramref name="currentDirectionDeg"/> by more than
    /// <paramref name="thresholdDeg"/>, or null when the wind holds steady.
    /// </summary>
    /// <param name="forecast">Time-ordered (time, direction, speed) triples.</param>
    public static WindShift? Detect(
        double currentDirectionDeg,
        IEnumerable<(DateTimeOffset Time, double? DirectionDeg, double? SpeedMs)> forecast,
        DateTimeOffset now,
        TimeSpan horizon,
        double thresholdDeg = DefaultThresholdDeg)
    {
        DateTimeOffset limit = now + horizon;

        foreach (var step in forecast.OrderBy(s => s.Time))
        {
            if (step.Time <= now || step.Time > limit)
            {
                continue;
            }

            if (step.DirectionDeg is not { } direction)
            {
                continue;
            }

            // Below roughly 1 m/s the reported direction is noise, not a shift.
            if (step.SpeedMs is { } speed && speed < 1.0)
            {
                continue;
            }

            double delta = SignedDifference(currentDirectionDeg, direction);

            if (Math.Abs(delta) >= thresholdDeg)
            {
                return new WindShift(
                    step.Time,
                    currentDirectionDeg,
                    direction,
                    delta,
                    delta >= 0 ? TurnDirection.Veering : TurnDirection.Backing);
            }
        }

        return null;
    }

    /// <summary>Strongest gust within the look-ahead window, or null without data.</summary>
    public static GustPeak? PeakGust(
        IEnumerable<(DateTimeOffset Time, double? GustMs)> forecast,
        DateTimeOffset now,
        TimeSpan horizon)
    {
        DateTimeOffset limit = now + horizon;

        var peak = forecast
            .Where(step => step.Time >= now && step.Time <= limit && step.GustMs is not null)
            .OrderByDescending(step => step.GustMs!.Value)
            .ThenBy(step => step.Time)
            .FirstOrDefault();

        return peak.GustMs is { } gust ? new GustPeak(peak.Time, gust) : null;
    }
}
