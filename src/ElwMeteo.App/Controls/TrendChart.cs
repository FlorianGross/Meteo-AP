using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ElwMeteo.App.Platform;
using ElwMeteo.Presentation.Charting;

namespace ElwMeteo.App.Controls;

/// <summary>
/// Draws the weather trend: a line panel for temperatures and, beneath it, a bar
/// panel for precipitation.
///
/// The two measures deliberately get their own panel and their own scale rather
/// than sharing one frame with two y-axes — a second axis makes crossings look
/// meaningful when they are an artefact of how the scales were chosen.
/// Rendering happens in OnRender against the live size, so nothing is distorted
/// by scaling a fixed-size drawing.
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(TrendChartModel),
        typeof(TrendChart),
        new FrameworkPropertyMetadata(
            TrendChartModel.Empty,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public TrendChartModel Model
    {
        get => (TrendChartModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    // Ink and surface tokens: text never wears a series colour.
    private static readonly Brush TextSecondary = Frozen(Color.FromRgb(0x93, 0xA1, 0xB1));
    private static readonly Brush TextPrimary = Frozen(Color.FromRgb(0xE6, 0xED, 0xF3));
    private static readonly Pen GridPen = FrozenPen(Color.FromArgb(0x33, 0x93, 0xA1, 0xB1), 1);
    private static readonly Pen ZeroPen = FrozenPen(Color.FromArgb(0x88, 0x93, 0xA1, 0xB1), 1);
    private static readonly Pen NowPen = FrozenPen(Color.FromArgb(0xAA, 0xE6, 0xED, 0xF3), 1);
    private static readonly Brush NightBrush = Frozen(Color.FromArgb(0x22, 0x93, 0xA1, 0xB1));

    private const double AxisWidth = 40;
    private const double TimeAxisHeight = 20;
    private const double PanelGap = 14;
    private const double BarPanelHeight = 62;

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        TrendChartModel model = Model;
        double width = ActualWidth;
        double height = ActualHeight;

        if (width < 60 || height < 60)
        {
            return;
        }

        if (!model.HasData)
        {
            DrawCentredText(context, "Keine Verlaufsdaten verfügbar.", width, height);
            return;
        }

        (DateTimeOffset start, DateTimeOffset end) = TimeRange(model);
        double totalSeconds = (end - start).TotalSeconds;
        if (totalSeconds <= 0)
        {
            return;
        }

        double plotLeft = AxisWidth;
        double plotWidth = width - AxisWidth;
        bool hasBars = model.Bars is not null && model.Bars.Points.Any(p => p.Value is > 0);

        double barTop = height - TimeAxisHeight - (hasBars ? BarPanelHeight : 0);
        double lineBottom = hasBars ? barTop - PanelGap : height - TimeAxisHeight;
        double lineTop = 6;

        double X(DateTimeOffset time) =>
            plotLeft + (time - start).TotalSeconds / totalSeconds * plotWidth;

        DrawNightSpans(context, model, X, lineTop, lineBottom, hasBars ? barTop + BarPanelHeight : lineBottom);
        DrawTimeAxis(context, start, end, X, height, lineTop);

        DrawLinePanel(context, model, X, plotLeft, width, lineTop, lineBottom);

        if (hasBars)
        {
            DrawBarPanel(context, model, X, plotLeft, barTop, BarPanelHeight, totalSeconds, plotWidth);
        }

        DrawNowMarker(context, model, X, lineTop, hasBars ? barTop + BarPanelHeight : lineBottom);
    }

    // ------------------------------------------------------------ line panel

    private static void DrawLinePanel(
        DrawingContext context,
        TrendChartModel model,
        Func<DateTimeOffset, double> x,
        double plotLeft,
        double width,
        double top,
        double bottom)
    {
        var values = model.Lines
            .SelectMany(l => l.Points)
            .Where(p => p.Value is not null)
            .Select(p => p.Value!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return;
        }

        // Round the range outwards to whole steps so the labels are readable.
        double rawMin = values.Min();
        double rawMax = values.Max();
        double step = NiceStep((rawMax - rawMin) / 4.0);
        double min = Math.Floor(rawMin / step) * step;
        double max = Math.Ceiling(rawMax / step) * step;

        if (Math.Abs(max - min) < 1e-9)
        {
            max = min + step;
        }

        double Y(double value) => bottom - (value - min) / (max - min) * (bottom - top);

        // Recessive gridlines with the value labels in the axis gutter.
        for (double value = min; value <= max + 1e-9; value += step)
        {
            double y = Y(value);
            context.DrawLine(GridPen, new Point(plotLeft, y), new Point(width, y));

            FormattedText label = Text(
                value.ToString(Math.Abs(step) < 1 ? "F1" : "F0", German), 10, TextSecondary);
            context.DrawText(label, new Point(plotLeft - label.Width - 6, y - label.Height / 2));
        }

        // Freezing point deserves its own emphasis in a fire-service context.
        if (min < 0 && max > 0)
        {
            double zeroY = Y(0);
            context.DrawLine(ZeroPen, new Point(plotLeft, zeroY), new Point(width, zeroY));
        }

        foreach (TrendSeries series in model.Lines)
        {
            DrawSeries(context, series, x, Y);
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        TrendSeries series,
        Func<DateTimeOffset, double> x,
        Func<double, double> y)
    {
        var pen = new Pen(series.Colour.ToBrush(), 2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        if (series.Dashed)
        {
            pen.DashStyle = new DashStyle([4, 3], 0);
        }

        pen.Freeze();

        var geometry = new StreamGeometry();
        using (StreamGeometryContext figure = geometry.Open())
        {
            bool open = false;

            foreach (TrendPoint point in series.Points)
            {
                if (point.Value is null)
                {
                    // A gap in the data must show as a gap, not as a straight line
                    // bridging it.
                    open = false;
                    continue;
                }

                var position = new Point(x(point.Time), y(point.Value.Value));

                if (!open)
                {
                    figure.BeginFigure(position, false, false);
                    open = true;
                }
                else
                {
                    figure.LineTo(position, true, true);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    // ------------------------------------------------------------- bar panel

    private static void DrawBarPanel(
        DrawingContext context,
        TrendChartModel model,
        Func<DateTimeOffset, double> x,
        double plotLeft,
        double top,
        double panelHeight,
        double totalSeconds,
        double plotWidth)
    {
        TrendBars bars = model.Bars!;
        double bottom = top + panelHeight;

        double peak = bars.Points.Max(p => p.Value ?? 0);
        if (peak <= 0)
        {
            return;
        }

        context.DrawLine(GridPen, new Point(plotLeft, bottom), new Point(plotLeft + plotWidth, bottom));

        FormattedText peakLabel = Text($"{peak.ToString("F1", German)} {bars.Unit}", 10, TextSecondary);
        context.DrawText(peakLabel, new Point(plotLeft - peakLabel.Width - 6, top));

        var brush = bars.Colour.ToBrush();
        brush.Freeze();

        // One slot per sample, with a 2 px surface gap so neighbours stay separate.
        double slot = bars.Points.Count > 1 ? plotWidth / bars.Points.Count : plotWidth;
        double barWidth = Math.Max(1.5, slot - 2);
        double radius = Math.Min(4, barWidth / 2);

        foreach (TrendPoint point in bars.Points)
        {
            double value = point.Value ?? 0;
            if (value <= 0)
            {
                continue;
            }

            double barHeight = value / peak * (panelHeight - 4);
            double left = x(point.Time) - barWidth / 2;

            context.DrawRoundedRectangle(
                brush, null,
                new Rect(left, bottom - barHeight, barWidth, barHeight),
                radius, radius);
        }
    }

    // ---------------------------------------------------------------- chrome

    private static void DrawNightSpans(
        DrawingContext context,
        TrendChartModel model,
        Func<DateTimeOffset, double> x,
        double top,
        double lineBottom,
        double bottom)
    {
        foreach ((DateTimeOffset from, DateTimeOffset to) in model.NightSpans)
        {
            double left = x(from);
            double right = x(to);

            if (right <= left)
            {
                continue;
            }

            context.DrawRectangle(NightBrush, null, new Rect(left, top, right - left, bottom - top));
        }
    }

    private static void DrawTimeAxis(
        DrawingContext context,
        DateTimeOffset start,
        DateTimeOffset end,
        Func<DateTimeOffset, double> x,
        double height,
        double top)
    {
        // A label every six hours keeps the axis readable at any sensible width.
        DateTimeOffset tick = new DateTimeOffset(
            start.Year, start.Month, start.Day, start.Hour, 0, 0, start.Offset);

        while (tick.Hour % 6 != 0)
        {
            tick = tick.AddHours(1);
        }

        for (; tick <= end; tick = tick.AddHours(6))
        {
            double position = x(tick);
            context.DrawLine(GridPen, new Point(position, top), new Point(position, height - TimeAxisHeight));

            string caption = tick.Hour == 0
                ? tick.ToString("dd.MM.", German)
                : tick.ToString("HH", German) + " Uhr";

            FormattedText label = Text(caption, 10, TextSecondary);
            context.DrawText(label, new Point(position - label.Width / 2, height - TimeAxisHeight + 4));
        }
    }

    private static void DrawNowMarker(
        DrawingContext context,
        TrendChartModel model,
        Func<DateTimeOffset, double> x,
        double top,
        double bottom)
    {
        double position = x(model.Now);
        context.DrawLine(NowPen, new Point(position, top), new Point(position, bottom));

        FormattedText label = Text("jetzt", 10, TextPrimary);
        context.DrawText(label, new Point(position + 4, top));
    }

    private static void DrawCentredText(DrawingContext context, string message, double width, double height)
    {
        FormattedText text = Text(message, 13, TextSecondary);
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    // ----------------------------------------------------------------- utils

    private static (DateTimeOffset Start, DateTimeOffset End) TimeRange(TrendChartModel model)
    {
        var times = model.Lines.SelectMany(l => l.Points).Select(p => p.Time).ToList();

        if (model.Bars is not null)
        {
            times.AddRange(model.Bars.Points.Select(p => p.Time));
        }

        return times.Count == 0
            ? (model.Now, model.Now.AddHours(1))
            : (times.Min(), times.Max());
    }

    /// <summary>Rounds an axis step to 1, 2, 5 or 10 times a power of ten.</summary>
    private static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw))
        {
            return 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalised = raw / magnitude;

        double step = normalised switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return step * magnitude;
    }

    private static FormattedText Text(string value, double size, Brush brush) =>
        new(value,
            German,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            1.0);

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color colour, double thickness)
    {
        var pen = new Pen(Frozen(colour), thickness);
        pen.Freeze();
        return pen;
    }
}
