namespace ElwMeteo.Core.Meteorology;

/// <summary>
/// Geometry of an estimated downwind hazard area: an inner exclusion circle plus
/// a cone opening downwind.
/// </summary>
public sealed record HazardArea(
    LatLon Origin,
    double DownwindBearingDeg,
    double HalfAngleDeg,
    double RangeMetres,
    double InnerRadiusMetres,
    IReadOnlyList<LatLon> ConeOutline,
    IReadOnlyList<LatLon> CentreLine);

/// <summary>
/// Builds a first-look downwind hazard area from wind direction and atmospheric
/// stability.
///
/// This is an orientation aid for the initial phase of an incident, in the spirit
/// of the FwDV 500 exclusion-zone rules of thumb. It is a cone on a sphere, not a
/// dispersion model: it knows nothing about terrain, buildings, source strength or
/// substance properties. Anything beyond the first few minutes belongs in a proper
/// dispersion calculation.
/// </summary>
public static class HazardPlume
{
    /// <summary>
    /// Lateral half-angle of the cone per stability class. Unstable air spreads a
    /// plume more widely across the wind; stable air keeps it narrow and long.
    /// </summary>
    public static double HalfAngleFor(PasquillClass stability) => stability switch
    {
        PasquillClass.A => 30.0,
        PasquillClass.B => 25.0,
        PasquillClass.C => 20.0,
        PasquillClass.D => 15.0,
        PasquillClass.E => 11.0,
        _ => 8.0
    };

    /// <summary>
    /// Suggested downwind extent in metres for the given stability class, based on
    /// the FwDV 500 baseline of a 50 m exclusion zone stretched downwind as
    /// vertical mixing weakens. Purely a starting point for the incident commander.
    /// </summary>
    public static double SuggestedRangeFor(PasquillClass stability) => stability switch
    {
        PasquillClass.A => 300.0,
        PasquillClass.B => 400.0,
        PasquillClass.C => 500.0,
        PasquillClass.D => 750.0,
        PasquillClass.E => 1_000.0,
        _ => 1_500.0
    };

    /// <summary>
    /// Build the hazard area.
    /// </summary>
    /// <param name="origin">Release point.</param>
    /// <param name="windFromDeg">Direction the wind comes from, degrees from north.</param>
    /// <param name="stability">Atmospheric stability class.</param>
    /// <param name="rangeMetres">Downwind extent; defaults to <see cref="SuggestedRangeFor"/>.</param>
    /// <param name="innerRadiusMetres">Radius of the all-round exclusion circle.</param>
    /// <param name="arcSegments">Resolution of the downwind arc.</param>
    public static HazardArea Build(
        LatLon origin,
        double windFromDeg,
        PasquillClass stability,
        double? rangeMetres = null,
        double innerRadiusMetres = 50.0,
        int arcSegments = 24)
    {
        double range = Math.Max(1.0, rangeMetres ?? SuggestedRangeFor(stability));
        double halfAngle = HalfAngleFor(stability);
        double downwind = WindScale.DownwindDirection(windFromDeg);
        int segments = Math.Max(4, arcSegments);

        var outline = new List<LatLon>(segments + 2)
        {
            // Start at the source, so the polygon closes into a wedge.
            origin
        };

        // Sweep the far arc from the left edge of the cone to the right.
        for (int i = 0; i <= segments; i++)
        {
            double fraction = (double)i / segments;
            double bearing = downwind - halfAngle + 2.0 * halfAngle * fraction;
            outline.Add(Geodesy.Destination(origin, bearing, range));
        }

        var centreLine = new List<LatLon>
        {
            origin,
            Geodesy.Destination(origin, downwind, range)
        };

        return new HazardArea(
            origin,
            downwind,
            halfAngle,
            range,
            Math.Max(0.0, innerRadiusMetres),
            outline,
            centreLine);
    }

    /// <summary>
    /// Time for the leading edge of the plume to reach a distance, assuming it
    /// travels at the 10 m wind speed. Returns null when the air is calm, because
    /// then there is no meaningful transport direction at all.
    /// </summary>
    public static TimeSpan? TravelTime(double distanceMetres, double windSpeedMs)
    {
        if (windSpeedMs <= 0.3)
        {
            return null;
        }

        return TimeSpan.FromSeconds(distanceMetres / windSpeedMs);
    }
}
