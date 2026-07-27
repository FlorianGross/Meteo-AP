namespace ElwMeteo.Core.Meteorology;

/// <summary>
/// Wind as a pair of components: <paramref name="U"/> eastward and
/// <paramref name="V"/> northward, both in m/s.
///
/// Meteorological reports name the direction the wind blows *from*, which is
/// useless for moving a particle across a map. Components are what an animation
/// or an interpolation actually needs.
/// </summary>
public readonly record struct WindVector(double U, double V)
{
    public double SpeedMs => Math.Sqrt(U * U + V * V);

    /// <summary>Direction the wind blows from, degrees from north.</summary>
    public double DirectionFromDeg =>
        SpeedMs < 1e-9 ? 0.0 : WindScale.Normalize(Math.Atan2(-U, -V) * 180.0 / Math.PI);

    /// <summary>Direction the air travels, degrees from north.</summary>
    public double DownwindDeg => WindScale.DownwindDirection(DirectionFromDeg);

    /// <summary>
    /// Builds components from a speed and the direction the wind comes from.
    /// A northerly (0°) blows towards the south, so V is negative.
    /// </summary>
    public static WindVector FromMeteorological(double speedMs, double directionFromDeg)
    {
        double radians = directionFromDeg * Math.PI / 180.0;
        return new WindVector(
            -speedMs * Math.Sin(radians),
            -speedMs * Math.Cos(radians));
    }

    /// <summary>Component-wise linear blend, used for interpolating between grid nodes.</summary>
    public static WindVector Lerp(WindVector a, WindVector b, double t) =>
        new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);
}
