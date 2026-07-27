using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ElwMeteo.Desktop.Platform;
using ElwMeteo.Presentation.Charting;

namespace ElwMeteo.Desktop.Controls;

/// <summary>
/// Draws the weather trend: a line panel for temperatures and, beneath it, a bar
/// panel for precipitation.
///
/// The two measures get their own panel and their own scale rather than sharing
/// one frame with two y-axes — a second axis makes crossings look meaningful
/// when they are an artefact of how the scales were chosen. Rendering happens
/// against the live size, so nothing is distorted by scaling a fixed drawing.
///
/// Same arrangement and the same colours as the Windows head; only the drawing
/// calls differ, because the decisions live in <see cref="TrendChartModel"/>.
/// </summary>
public sealed class TrendChart : Control
{
    public static readonly StyledProperty<TrendChartModel> ModelProperty =
        AvaloniaProperty.Register<TrendChart, TrendChartModel>(nameof(Model), TrendChartModel.Empty);

    static TrendChart()
    {
        AffectsRender<TrendChart>(ModelProperty);
    }

    public TrendChartModel Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(0x93, 0xA1, 0xB1));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x93, 0xA1, 0xB1)), 1);
    private static readonly IPen ZeroPen = new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x93, 0xA1, 0xB1)), 1);
    private static readonly IPen NowPen = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0xE6, 0xED, 0xF3)), 1);
    private static readonly IBrush NightBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x93, 0xA1, 0xB1));

    private const double PlotLeft = 46;
    private const double AxisHeight = 20;
    private const double PanelGap = 14;

    public override void Render(DrawingContext context)
    {
        TrendChartModel model = Model;
        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width < 80 || height < 80 || !model.HasData)
        {
            return;
        }

        bool hasBars = model.Bars is not null && model.Bars.Points.Any(p => p.Value is > 0);

        double barPanelHeight = hasBars ? Math.Min(110, height * 0.32) : 0;
        double linePanelHeight = height - barPanelHeight - AxisHeight - (hasBars ? PanelGap : 0);

        (DateTimeOffset from, DateTimeOffset to) = TimeRange(model);
        double span = (to - from).TotalMinutes;

        if (span <= 0)
        {
            return;
        }

        double X(DateTimeOffset t) => PlotLeft + (t - from).TotalMinutes / span * (width - PlotLeft);

        DrawNight(context, model, X, linePanelHeight + (hasBars ? PanelGap + barPanelHeight : 0));
        DrawLinePanel(context, model, X, width, linePanelHeight);

        if (hasBars)
        {
            DrawBarPanel(context, model.Bars!, X, width,
                linePanelHeight + PanelGap, barPanelHeight, span);
        }

        DrawTimeAxis(context, model, X, height - AxisHeight);

        // The current time, so "in three hours" is read off rather than counted.
        double nowX = X(model.Now);
        if (nowX >= PlotLeft && nowX <= width)
        {
            context.DrawLine(NowPen, new Point(nowX, 0), new Point(nowX, height - AxisHeight));
        }
    }

    private static (DateTimeOffset From, DateTimeOffset To) TimeRange(TrendChartModel model)
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

    private static void DrawNight(
        DrawingContext context, TrendChartModel model, Func<DateTimeOffset, double> x, double height)
    {
        foreach ((DateTimeOffset from, DateTimeOffset to) in model.NightSpans)
        {
            double left = x(from);
            double right = x(to);

            if (right <= left)
            {
                continue;
            }

            context.FillRectangle(NightBrush, new Rect(left, 0, right - left, height));
        }
    }

    private void DrawLinePanel(
        DrawingContext context, TrendChartModel model, Func<DateTimeOffset, double> x,
        double width, double height)
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

        double min = Math.Floor(values.Min() - 1);
        double max = Math.Ceiling(values.Max() + 1);

        if (max - min < 2)
        {
            max = min + 2;
        }

        double Y(double v) => height - (v - min) / (max - min) * height;

        // Grid and scale, roughly five steps.
        double step = NiceStep((max - min) / 5);

        for (double v = Math.Ceiling(min / step) * step; v <= max; v += step)
        {
            double y = Y(v);
            context.DrawLine(GridPen, new Point(PlotLeft, y), new Point(width, y));

            FormattedText label = Label($"{v.ToString("F0", German)} {model.LineUnit}");
            context.DrawText(label, new Point(PlotLeft - label.Width - 6, y - label.Height / 2));
        }

        // Freezing gets its own line: it decides whether roads are gritted.
        if (min < 0 && max > 0)
        {
            double zeroY = Y(0);
            context.DrawLine(ZeroPen, new Point(PlotLeft, zeroY), new Point(width, zeroY));
        }

        foreach (TrendSeries series in model.Lines)
        {
            var pen = new Pen(series.Colour.ToBrush(), 2)
            {
                DashStyle = series.Dashed ? new DashStyle([4, 3], 0) : null,
                LineJoin = PenLineJoin.Round
            };

            var geometry = new StreamGeometry();

            using (StreamGeometryContext geo = geometry.Open())
            {
                bool open = false;

                foreach (TrendPoint point in series.Points)
                {
                    if (point.Value is null)
                    {
                        // A gap in the data must stay a gap, not a straight line
                        // across it.
                        open = false;
                        continue;
                    }

                    var position = new Point(x(point.Time), Y(point.Value.Value));

                    if (!open)
                    {
                        geo.BeginFigure(position, false);
                        open = true;
                    }
                    else
                    {
                        geo.LineTo(position);
                    }
                }

                if (open)
                {
                    geo.EndFigure(false);
                }
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawBarPanel(
        DrawingContext context, TrendBars bars, Func<DateTimeOffset, double> x,
        double width, double top, double height, double spanMinutes)
    {
        double max = Math.Max(1.0, bars.Points.Max(p => p.Value ?? 0));
        IBrush brush = bars.Colour.ToBrush();

        // One slot per point, minus a hairline so neighbours stay readable.
        double slot = Math.Max(2.0, (width - PlotLeft) / Math.Max(1, bars.Points.Count) - 1);

        foreach (TrendPoint point in bars.Points)
        {
            if (point.Value is not > 0)
            {
                continue;
            }

            double barHeight = point.Value.Value / max * height;
            double left = x(point.Time) - slot / 2;

            context.FillRectangle(brush, new Rect(left, top + height - barHeight, slot, barHeight));
        }

        context.DrawLine(GridPen, new Point(PlotLeft, top + height), new Point(width, top + height));

        FormattedText label = Label($"{max.ToString("F1", German)} {bars.Unit}");
        context.DrawText(label, new Point(PlotLeft - label.Width - 6, top - label.Height / 2));
    }

    private void DrawTimeAxis(
        DrawingContext context, TrendChartModel model, Func<DateTimeOffset, double> x, double top)
    {
        (DateTimeOffset from, DateTimeOffset to) = TimeRange(model);

        // Midnight and midday: enough to orient without crowding the axis.
        DateTimeOffset cursor = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, from.Offset);

        while (cursor <= to)
        {
            if (cursor >= from && (cursor.Hour == 0 || cursor.Hour == 12))
            {
                double position = x(cursor);
                FormattedText label = Label(cursor.Hour == 0
                    ? cursor.ToString("dd.MM.", German)
                    : cursor.ToString("HH:mm", German));

                context.DrawText(label, new Point(position - label.Width / 2, top + 2));
            }

            cursor = cursor.AddHours(6);
        }
    }

    private FormattedText Label(string text) =>
        new(text,
            German,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10.5,
            TextSecondary);

    /// <summary>Rounds a raw step up to something a human would have chosen.</summary>
    private static double NiceStep(double raw)
    {
        double[] candidates = [1, 2, 2.5, 5, 10, 20, 25, 50, 100];
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 0.1))));

        foreach (double candidate in candidates)
        {
            if (candidate * magnitude >= raw)
            {
                return candidate * magnitude;
            }
        }

        return magnitude * 10;
    }
}
