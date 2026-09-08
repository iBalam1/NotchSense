using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NotchSense;

public sealed class RingControl : FrameworkElement
{
    private const double Diameter = 44;
    private const double Stroke = 4;

    public string Metric { get; set; } = string.Empty;
    public MetricReading Reading { get; private set; } = MetricReading.Unavailable(string.Empty);

    public RingControl()
    {
        Width = 58;
        Height = 68;
        Margin = new Thickness(0, 3, 0, 3);
        Cursor = Cursors.Hand;
        ToolTip = null;
    }

    public void SetReading(MetricReading reading)
    {
        Reading = reading;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(ActualWidth / 2, 25);
        var radius = (Diameter - Stroke) / 2;
        var fill = new SolidColorBrush(Color.FromRgb(42, 42, 42));
        fill.Freeze();
        dc.DrawEllipse(fill, null, center, radius, radius);
        var track = new Pen(new SolidColorBrush(Color.FromRgb(58, 58, 58)), Stroke);
        track.Freeze();
        dc.DrawEllipse(null, track, center, radius, radius);

        if (Reading.Percent is { } percent && percent > 0)
        {
            var accent = new Pen(new SolidColorBrush(Reading.Color), Stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            accent.Freeze();
            if (percent >= 99.99)
            {
                dc.DrawEllipse(null, accent, center, radius, radius);
            }
            else
            {
                var endAngle = -90 + Math.Clamp(percent, 0, 100) * 3.6;
                var geometry = new StreamGeometry();
                using var context = geometry.Open();
                context.BeginFigure(PointAt(center, radius, -90), false, false);
                context.ArcTo(PointAt(center, radius, endAngle), new Size(radius, radius), 0,
                    percent > 50, SweepDirection.Clockwise, true, false);
                geometry.Freeze();
                dc.DrawGeometry(null, accent, geometry);
            }
        }

        DrawText(dc, Metric, 9, FontWeights.SemiBold, center.X, center.Y, Brushes.White);
        DrawText(dc, Reading.DisplayPercent, 14, FontWeights.SemiBold, center.X, 57, Brushes.White);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Width, Height);

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static void DrawText(DrawingContext dc, string text, double size, FontWeight weight, double x, double y, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
        dc.DrawText(formatted, new Point(x - formatted.Width / 2, y - formatted.Height / 2));
    }
}
