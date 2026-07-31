using System.Globalization;
using System.Text;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Reporting;

/// <summary>
/// Draws the temperature and precipitation curve as inline SVG for the printed
/// report.
///
/// SVG rather than a raster image on purpose: it goes straight into the HTML
/// with no file to lose, it prints at the printer's resolution instead of the
/// screen's, and it survives being saved as PDF as real vector lines. It also
/// costs no dependency, which matters for a program that has to build for
/// Windows, macOS and Linux from the same source.
///
/// The colours are the print set, not the dark-theme set: this ends up on white
/// paper, quite possibly in a black-and-white printer, so the two lines differ
/// in dash pattern as well as in hue.
/// </summary>
internal static class ReportChart
{
    private const int Width = 1000;
    private const int Height = 260;
    private const int LeftMargin = 46;
    private const int RightMargin = 46;
    private const int TopMargin = 14;
    private const int BottomMargin = 30;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Builds the chart, or an empty string when there is nothing to draw — a
    /// blank set of axes on a report would suggest the data was flat rather
    /// than absent.
    /// </summary>
    internal static string Build(IReadOnlyList<HourlyStep> hourly, DateTimeOffset now)
    {
        var points = hourly
            .Where(h => h.TemperatureC is not null)
            .OrderBy(h => h.Time)
            .ToList();

        if (points.Count < 2)
        {
            return string.Empty;
        }

        DateTimeOffset from = points[0].Time;
        DateTimeOffset to = points[^1].Time;
        double span = (to - from).TotalMinutes;

        if (span <= 0)
        {
            return string.Empty;
        }

        double minimum = points.Min(p => p.TemperatureC!.Value);
        double maximum = points.Max(p => p.TemperatureC!.Value);

        // Dew point shares the axis, so it has to be inside the range too.
        foreach (double dew in points.Select(DewPointOf).OfType<double>())
        {
            minimum = Math.Min(minimum, dew);
            maximum = Math.Max(maximum, dew);
        }

        // A degree of headroom, and never a zero-height band on a flat day.
        minimum = Math.Floor(minimum) - 1;
        maximum = Math.Ceiling(maximum) + 1;

        if (maximum - minimum < 4)
        {
            maximum = minimum + 4;
        }

        double plotWidth = Width - LeftMargin - RightMargin;
        double plotHeight = Height - TopMargin - BottomMargin;

        double X(DateTimeOffset time) => LeftMargin + ((time - from).TotalMinutes / span * plotWidth);
        double Y(double value) => TopMargin + ((maximum - value) / (maximum - minimum) * plotHeight);

        var svg = new StringBuilder();
        svg.Append(Invariant, $"<svg viewBox=\"0 0 {Width} {Height}\" class=\"chart\" role=\"img\" ");
        svg.Append("aria-label=\"Temperatur- und Niederschlagsverlauf\">");

        // ---------------------------------------------------- grid and axes
        for (int step = 0; step <= 4; step++)
        {
            double value = minimum + ((maximum - minimum) * step / 4.0);
            double y = Y(value);

            svg.Append(Invariant,
                $"<line x1=\"{LeftMargin}\" y1=\"{y:F1}\" x2=\"{Width - RightMargin}\" y2=\"{y:F1}\" class=\"grid\" />");
            svg.Append(Invariant,
                $"<text x=\"{LeftMargin - 6}\" y=\"{y + 3.5:F1}\" class=\"axis end\">{value.ToString("F0", German)}°</text>");
        }

        // Zero degrees gets its own line: frost is the one threshold on this
        // chart that changes what gets ordered.
        if (minimum < 0 && maximum > 0)
        {
            svg.Append(Invariant,
                $"<line x1=\"{LeftMargin}\" y1=\"{Y(0):F1}\" x2=\"{Width - RightMargin}\" y2=\"{Y(0):F1}\" class=\"freeze\" />");
        }

        // ----------------------------------------------------- night shading
        foreach ((DateTimeOffset start, DateTimeOffset end) in NightSpans(points))
        {
            double x1 = X(Clamp(start, from, to));
            double x2 = X(Clamp(end, from, to));

            if (x2 - x1 > 0.5)
            {
                svg.Append(Invariant,
                    $"<rect x=\"{x1:F1}\" y=\"{TopMargin}\" width=\"{x2 - x1:F1}\" " +
                    $"height=\"{plotHeight:F1}\" class=\"night\" />");
            }
        }

        // --------------------------------------------------- precipitation
        double peak = points.Max(p => p.PrecipitationMm ?? 0.0);

        if (peak > 0)
        {
            double barWidth = Math.Max(2.0, plotWidth / points.Count * 0.7);

            foreach (HourlyStep point in points.Where(p => p.PrecipitationMm is > 0))
            {
                double height = point.PrecipitationMm!.Value / peak * (plotHeight * 0.45);
                double x = X(point.Time) - (barWidth / 2);

                svg.Append(Invariant,
                    $"<rect x=\"{x:F1}\" y=\"{TopMargin + plotHeight - height:F1}\" " +
                    $"width=\"{barWidth:F1}\" height=\"{height:F1}\" class=\"rain\" />");
            }

            svg.Append(Invariant,
                $"<text x=\"{Width - RightMargin}\" y=\"{TopMargin + 10}\" class=\"axis end rain-label\">" +
                $"Niederschlag bis {peak.ToString("F1", German)} mm/h</text>");
        }

        // ---------------------------------------------------------- curves
        svg.Append(Invariant,
            $"<polyline points=\"{Path(points, p => p.TemperatureC, X, Y)}\" class=\"temperature\" />");

        string dewPath = Path(points, DewPointOf, X, Y);

        if (dewPath.Length > 0)
        {
            svg.Append(Invariant, $"<polyline points=\"{dewPath}\" class=\"dewpoint\" />");
        }

        // ------------------------------------------------------- now marker
        if (now >= from && now <= to)
        {
            double x = X(now);
            svg.Append(Invariant,
                $"<line x1=\"{x:F1}\" y1=\"{TopMargin}\" x2=\"{x:F1}\" y2=\"{TopMargin + plotHeight:F1}\" class=\"now\" />");
            svg.Append(Invariant,
                $"<text x=\"{x + 4:F1}\" y=\"{TopMargin + 10}\" class=\"axis\">jetzt</text>");
        }

        // -------------------------------------------------------- time axis
        DateTimeOffset tick = new(
            from.Year, from.Month, from.Day, from.Hour, 0, 0, from.Offset);

        while (tick <= to)
        {
            if (tick >= from && tick.Hour % 6 == 0)
            {
                double x = X(tick);
                svg.Append(Invariant,
                    $"<line x1=\"{x:F1}\" y1=\"{TopMargin + plotHeight:F1}\" x2=\"{x:F1}\" " +
                    $"y2=\"{TopMargin + plotHeight + 4:F1}\" class=\"grid\" />");
                svg.Append(Invariant,
                    $"<text x=\"{x:F1}\" y=\"{Height - 14}\" class=\"axis middle\">" +
                    $"{tick.ToLocalTime().ToString("HH", German)} Uhr</text>");
                svg.Append(Invariant,
                    $"<text x=\"{x:F1}\" y=\"{Height - 3}\" class=\"axis middle faint\">" +
                    $"{tick.ToLocalTime().ToString("dd.MM.", German)}</text>");
            }

            tick = tick.AddHours(1);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>
    /// A polyline path. Gaps in the data break the line rather than being bridged
    /// across, so a missing night does not read as a straight interpolation.
    /// </summary>
    private static string Path(
        IReadOnlyList<HourlyStep> points,
        Func<HourlyStep, double?> select,
        Func<DateTimeOffset, double> x,
        Func<double, double> y)
    {
        var parts = new List<string>();

        foreach (HourlyStep point in points)
        {
            if (select(point) is { } value)
            {
                parts.Add(string.Format(Invariant, "{0:F1},{1:F1}", x(point.Time), y(value)));
            }
        }

        return parts.Count < 2 ? string.Empty : string.Join(' ', parts);
    }

    private static double? DewPointOf(HourlyStep step) =>
        step.TemperatureC is { } temperature && step.RelativeHumidityPercent is { } humidity
            ? Meteorology.Thermodynamics.DewPointC(temperature, humidity)
            : null;

    /// <summary>
    /// Rough night bands, from the hours themselves rather than a solar
    /// calculation: the report only needs to show which part of the curve is
    /// after dark, and a band that is half an hour out changes nothing.
    /// </summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> NightSpans(IReadOnlyList<HourlyStep> points)
    {
        var spans = new List<(DateTimeOffset, DateTimeOffset)>();
        DateTimeOffset first = points[0].Time.ToLocalTime();

        for (DateTimeOffset day = first.Date.AddDays(-1); day <= points[^1].Time.ToLocalTime().Date; day = day.AddDays(1))
        {
            var start = new DateTimeOffset(day.Year, day.Month, day.Day, 21, 0, 0, first.Offset);
            spans.Add((start, start.AddHours(9)));
        }

        return spans;
    }

    private static DateTimeOffset Clamp(DateTimeOffset value, DateTimeOffset low, DateTimeOffset high) =>
        value < low ? low : value > high ? high : value;
}
