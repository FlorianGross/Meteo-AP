using ElwMeteo.Core.Meteorology;

namespace ElwMeteo.Core.Models;

/// <summary>
/// A square wind grid flattened row-major, north row first, ready to be handed to
/// the map's particle animation.
/// </summary>
public sealed record WindGrid(
    int Size,
    double North,
    double South,
    double West,
    double East,
    double[] U,
    double[] V,
    double MaxSpeedMs)
{
    /// <summary>
    /// Bilinearly interpolated wind at a position. Outside the grid the nearest
    /// edge value is used, which keeps particles that drift off the edge moving
    /// plausibly instead of stalling.
    /// </summary>
    public WindVector Sample(double latitude, double longitude)
    {
        // Fractional node coordinates; row 0 is the northern edge.
        double rowSpan = North - South;
        double columnSpan = East - West;

        double row = rowSpan <= 0 ? 0 : (North - latitude) / rowSpan * (Size - 1);
        double column = columnSpan <= 0 ? 0 : (longitude - West) / columnSpan * (Size - 1);

        row = Math.Clamp(row, 0, Size - 1);
        column = Math.Clamp(column, 0, Size - 1);

        int row0 = (int)Math.Floor(row);
        int column0 = (int)Math.Floor(column);
        int row1 = Math.Min(row0 + 1, Size - 1);
        int column1 = Math.Min(column0 + 1, Size - 1);

        double rowFraction = row - row0;
        double columnFraction = column - column0;

        WindVector top = WindVector.Lerp(At(row0, column0), At(row0, column1), columnFraction);
        WindVector bottom = WindVector.Lerp(At(row1, column0), At(row1, column1), columnFraction);

        return WindVector.Lerp(top, bottom, rowFraction);
    }

    private WindVector At(int row, int column)
    {
        int index = row * Size + column;
        return new WindVector(U[index], V[index]);
    }
}

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

    /// <summary>Nodes per side, derived from the point count.</summary>
    public int Size => (int)Math.Round(Math.Sqrt(Points.Count));

    /// <summary>
    /// The field as a regular grid of components plus its bounding box — the
    /// shape a particle animation needs in order to interpolate.
    ///
    /// Nodes without data borrow the field's mean vector rather than a zero, so a
    /// single gap does not punch a dead calm into the middle of the animation.
    /// Returns null when the grid is not square or holds no usable node at all.
    /// </summary>
    public WindGrid? ToGrid()
    {
        int size = Size;
        if (size < 2 || size * size != Points.Count)
        {
            return null;
        }

        var usable = Points.Where(p => p.HasData).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        WindVector fallback = new(
            usable.Average(p => WindVector.FromMeteorological(p.SpeedMs!.Value, p.DirectionDeg!.Value).U),
            usable.Average(p => WindVector.FromMeteorological(p.SpeedMs!.Value, p.DirectionDeg!.Value).V));

        var u = new double[Points.Count];
        var v = new double[Points.Count];

        for (int i = 0; i < Points.Count; i++)
        {
            WindFieldPoint point = Points[i];
            WindVector vector = point.HasData
                ? WindVector.FromMeteorological(point.SpeedMs!.Value, point.DirectionDeg!.Value)
                : fallback;

            u[i] = vector.U;
            v[i] = vector.V;
        }

        return new WindGrid(
            size,
            North: Points.Max(p => p.Latitude),
            South: Points.Min(p => p.Latitude),
            West: Points.Min(p => p.Longitude),
            East: Points.Max(p => p.Longitude),
            U: u,
            V: v,
            MaxSpeedMs: usable.Max(p => p.SpeedMs!.Value));
    }

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
