using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Presentation.Charting;

/// <summary>One point of a trend series.</summary>
public sealed record TrendPoint(DateTimeOffset Time, double? Value);

/// <summary>A named line series with its own colour.</summary>
public sealed record TrendSeries(string Name, UiColour Colour, IReadOnlyList<TrendPoint> Points, bool Dashed = false);

/// <summary>A named bar series, drawn on its own panel.</summary>
public sealed record TrendBars(string Name, UiColour Colour, IReadOnlyList<TrendPoint> Points, string Unit);

/// <summary>
/// Everything the chart draws.
///
/// Deliberately free of any drawing type: the arrangement of the chart — which
/// series, which colour, where the night falls — is a decision about the data,
/// and both the WPF and the Avalonia renderer read the same answer. Only the
/// act of putting pixels on the screen differs between them.
/// </summary>
public sealed record TrendChartModel(
    IReadOnlyList<TrendSeries> Lines,
    TrendBars? Bars,
    IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> NightSpans,
    DateTimeOffset Now,
    string LineUnit)
{
    public static TrendChartModel Empty { get; } = new([], null, [], DateTimeOffset.UnixEpoch, string.Empty);

    public bool HasData => Lines.Any(l => l.Points.Any(p => p.Value is not null));
}
