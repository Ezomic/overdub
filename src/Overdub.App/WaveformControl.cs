using System.Windows;
using System.Windows.Media;

namespace Overdub.App;

public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(object), typeof(WaveformControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(WaveformControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public object? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        if (Peaks is not float[] { Length: > 0 } peaks || ActualWidth < 1)
        {
            return;
        }

        var pen = new Pen(Brush, 1);
        var middle = ActualHeight / 2;
        var columns = (int)ActualWidth;
        for (var x = 0; x < columns; x++)
        {
            var from = (int)((long)x * peaks.Length / columns);
            var to = Math.Max(from + 1, (int)((long)(x + 1) * peaks.Length / columns));
            var peak = 0f;
            for (var i = from; i < Math.Min(to, peaks.Length); i++)
            {
                peak = Math.Max(peak, peaks[i]);
            }

            var half = Math.Max(1, Math.Min(1f, peak) * (middle - 2));
            context.DrawLine(pen, new Point(x + 0.5, middle - half), new Point(x + 0.5, middle + half));
        }
    }
}
