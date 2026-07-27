namespace ElwMeteo.Core.Models;

/// <summary>One 15-minute step of the short-range nowcast.</summary>
public sealed record NowcastStep(
    DateTimeOffset Time,
    double? PrecipitationMm,
    double? RainMm,
    double? SnowfallCm,
    double? WindSpeedMs,
    double? WindGustMs,
    double? WindDirectionDeg,
    double? TemperatureC,
    double? CapeJkg,
    double? LightningPotential,
    int? WeatherCode)
{
    public bool HasPrecipitation => PrecipitationMm is > 0.0;
}

/// <summary>One hour of the forecast.</summary>
public sealed record HourlyStep(
    DateTimeOffset Time,
    double? TemperatureC,
    double? PrecipitationMm,
    int? PrecipitationProbabilityPercent,
    double? WindSpeedMs,
    double? WindGustMs,
    double? WindDirectionDeg,
    double? CloudCoverPercent,
    double? RelativeHumidityPercent,
    double? VisibilityM,
    double? CapeJkg,
    int? WeatherCode);

/// <summary>Daily aggregates, used for the sun/UV strip and the day's extremes.</summary>
public sealed record DailySummary(
    DateTimeOffset Date,
    double? TemperatureMinC,
    double? TemperatureMaxC,
    double? PrecipitationSumMm,
    double? UvIndexMax,
    double? WindGustMaxMs);

/// <summary>
/// Everything the app knows about the weather at one position at one moment,
/// straight from the provider and before any interpretation.
/// </summary>
public sealed record WeatherSnapshot
{
    public required GeoPosition Position { get; init; }

    public required DateTimeOffset RetrievedAtUtc { get; init; }

    public DateTimeOffset? ObservationTime { get; init; }

    public double? TemperatureC { get; init; }

    public double? ApparentTemperatureC { get; init; }

    public double? DewPointC { get; init; }

    public double? RelativeHumidityPercent { get; init; }

    public double? PressureMslHpa { get; init; }

    public double? SurfacePressureHpa { get; init; }

    public double? WindSpeedMs { get; init; }

    public double? WindGustMs { get; init; }

    public double? WindDirectionDeg { get; init; }

    public double? CloudCoverPercent { get; init; }

    public double? VisibilityM { get; init; }

    public double? PrecipitationMm { get; init; }

    public double? SnowfallCm { get; init; }

    public double? CapeJkg { get; init; }

    public int? WeatherCode { get; init; }

    public bool IsDay { get; init; }

    public double? ElevationM { get; init; }

    /// <summary>Name of the numerical model behind the data, for the source line.</summary>
    public string ModelName { get; init; } = "unbekannt";

    public IReadOnlyList<NowcastStep> Nowcast { get; init; } = [];

    public IReadOnlyList<HourlyStep> Hourly { get; init; } = [];

    public IReadOnlyList<DailySummary> Daily { get; init; } = [];

    /// <summary>Age of the data, to grey out the panel once it goes stale.</summary>
    public TimeSpan AgeAt(DateTimeOffset now) => now - RetrievedAtUtc;
}
