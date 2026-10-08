using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Overdub.App;

public sealed class RulerControl : FrameworkElement
{
    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var line = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var panel = TryFindResource("Panel") as Brush ?? Brushes.Black;
        context.DrawRectangle(panel, null, new Rect(0, 0, ActualWidth, ActualHeight));
        context.DrawLine(new Pen(line, 1), new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));
        var pen = new Pen(dim, 1);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var second = 0; second * Timeline.PixelsPerSecond < ActualWidth; second++)
        {
            var x = (second * Timeline.PixelsPerSecond) + 0.5;
            var major = second % 5 == 0;
            context.DrawLine(pen, new Point(x, ActualHeight), new Point(x, ActualHeight - (major ? 12 : 6)));
            if (!major)
            {
                continue;
            }

            var label = new FormattedText($"{second / 60}:{second % 60:00}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, dim, dpi);
            context.DrawText(label, new Point(x + 4, 3));
        }
    }
}
