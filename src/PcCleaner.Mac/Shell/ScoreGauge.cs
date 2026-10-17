using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PcCleaner.Shell;

/// <summary>The score ring of the dashboard: a track circle and an arc from the top, clockwise, for Value of 100.</summary>
public sealed class ScoreGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ScoreGauge, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<ScoreGauge, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<ScoreGauge, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<ScoreGauge, double>(nameof(Thickness), 10);

    static ScoreGauge() => AffectsRender<ScoreGauge>(ValueProperty, StrokeProperty, TrackBrushProperty, ThicknessProperty);

    /// <summary>0..100.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness * 2)
            return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Thickness) / 2;
        if (TrackBrush is { } track)
            context.DrawEllipse(null, new Pen(track, Thickness), center, radius, radius);

        var fraction = Math.Clamp(Value / 100, 0, 1);
        if (fraction <= 0 || Stroke is not { } stroke)
            return;
        var pen = new Pen(stroke, Thickness, lineCap: PenLineCap.Round);
        if (fraction >= 0.9999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, arc);
    }
}
