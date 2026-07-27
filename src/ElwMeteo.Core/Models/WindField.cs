using ElwMeteo.Core.Meteorology;

namespace ElwMeteo.Core.Models;

/// <summary>Wind at one grid node of the surrounding area.</summary>
public sealed record WindFieldPoint(
    double Latitude,
    double Longitude,
    double? SpeedMs,
    double? GustMs,
    double? DirectionDeg)
{
    /// <summary>Bearing the air travels — the way an arrow at this node should point.</summary>
    public double? DownwindDeg => DirectionDeg is { } from ? WindScale.DownwindDirection(from) : null;

    public bool HasData => SpeedMs is not null && DirectionDeg is not null;
}

/// <summary>
/// A square grid of wind vectors around the incident, retrieved in one request.
///
/// A single reading at the vehicle says nothing about the wind two streets over,
/// where a valley or a ridge can turn the plume. The grid makes that visible.
/// </summary>
public sealed record WindField(
    DateTimeOffset RetrievedAtUtc,
    double SpacingMetres,
    IReadOnlyList<WindFieldPoint> Points)
{
    public static WindField Empty { get; } = new(DateTimeOffset.MinValue, 0, []);

    public bool IsEmpty => Points.Count == 0;

    /// <summary>Spread between the calmest and windiest node, in m/s.</summary>
    public double? SpeedSpreadMs
    {
        get
        {
            var speeds = Points.Where(p => p.SpeedMs is not null).Select(p => p.SpeedMs!.Value).ToList();
            return speeds.Count < 2 ? null : speeds.Max() - speeds.Min();
        }
    }

    /// <summary>
    /// Largest angular disagreement between any two nodes. A large value means
    /// the terrain is steering the flow and a single-direction plume estimate is
    /// not to be trusted.
    /// </summary>
    public double? DirectionSpreadDeg
    {
        get
        {
            var directions = Points.Where(p => p.DirectionDeg is not null)
                .Select(p => p.DirectionDeg!.Value)
                .ToList();

            if (directions.Count < 2)
            {
                return null;
            }

            double maxSpread = 0;
            for (int i = 0; i < directions.Count; i++)
            {
                for (int j = i + 1; j < directions.Count; j++)
                {
                    double spread = Math.Abs(WindShiftDetector.SignedDifference(directions[i], directions[j]));
                    maxSpread = Math.Max(maxSpread, spread);
                }
            }

            return maxSpread;
        }
    }
}
