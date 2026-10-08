using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overdub.App;

public readonly record struct GridInfo(double PixelsPerBeat, int BeatsPerBar);

public sealed class RulerControl : FrameworkElement
{
    public static readonly DependencyProperty TimeGridProperty = DependencyProperty.Register(
        nameof(TimeGrid), typeof(object), typeof(RulerControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public object? TimeGrid
    {
        get => GetValue(TimeGridProperty);
        set => SetValue(TimeGridProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var line = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var panel = TryFindResource("Panel") as Brush ?? Brushes.Black;
        context.DrawRectangle(panel, null, new Rect(0, 0, ActualWidth, ActualHeight));
        context.DrawLine(new Pen(line, 1), new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));
        if (TimeGrid is not GridInfo { PixelsPerBeat: > 0.5 } grid)
        {
            return;
        }

        var pen = new Pen(dim, 1);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var barWidth = grid.PixelsPerBeat * grid.BeatsPerBar;
        var labelEvery = 1;
        while (labelEvery * barWidth < 36)
        {
            labelEvery *= 2;
        }

        var beats = (int)(ActualWidth / grid.PixelsPerBeat) + 1;
        for (var beat = 0; beat < beats; beat++)
        {
            var x = (beat * grid.PixelsPerBeat) + 0.5;
            var isBar = beat % grid.BeatsPerBar == 0;
            if (!isBar && grid.PixelsPerBeat < 7)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, ActualHeight), new Point(x, ActualHeight - (isBar ? 14 : 5)));
            var bar = (beat / grid.BeatsPerBar) + 1;
            if (isBar && (bar - 1) % labelEvery == 0)
            {
                var label = new FormattedText(bar.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, dim, dpi);
                context.DrawText(label, new Point(x + 4, 3));
            }
        }
    }
}
