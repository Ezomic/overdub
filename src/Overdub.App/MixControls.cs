using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed record MixBarItem(string Name, double Current, double Target, Color Color);

public sealed class MixBarsControl : FrameworkElement
{
    public IReadOnlyList<MixBarItem> Items { get; set; } = [];

    public bool IsPan { get; set; }

    public static Color RoleColor(MixRole role) => role switch
    {
        MixRole.Drums => Color.FromRgb(0x4C, 0xC3, 0xA0),
        MixRole.Bass or MixRole.AudioBass => Color.FromRgb(0x6B, 0x8D, 0xE3),
        MixRole.Guitar or MixRole.AudioGuitar => Color.FromRgb(0xB4, 0x8B, 0xE3),
        MixRole.Lead => Color.FromRgb(0xE3, 0x6B, 0x8D),
        MixRole.Keys => Color.FromRgb(0xE6, 0xA2, 0x3C),
        _ => Color.FromRgb(0x9A, 0x9A, 0xA5),
    };

    public double RowHeight => 30;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, Math.Max(30, Items.Count * RowHeight) + 8);

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var text = TryFindResource("Text") as Brush ?? Brushes.White;
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        const double left = 130;
        var right = ActualWidth - 20;
        var width = Math.Max(10, right - left);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var y = 4 + (i * RowHeight) + (RowHeight / 2);
            var label = new FormattedText(item.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12.5, text, dpi);
            context.DrawText(label, new Point(14, y - (label.Height / 2)));
            var track = new Rect(left, y - 3, width, 6);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31)), null, track, 3, 3);
            var color = new SolidColorBrush(item.Color);
            if (IsPan)
            {
                var mid = left + (width / 2);
                context.DrawLine(new Pen(dim, 1), new Point(mid, y - 8), new Point(mid, y + 8));
                var current = left + ((item.Current + 1) / 2 * width);
                var target = left + ((item.Target + 1) / 2 * width);
                context.DrawEllipse(null, new Pen(color, 2), new Point(current, y), 7, 7);
                if (Math.Abs(item.Target - item.Current) > 0.001)
                {
                    context.DrawEllipse(color, null, new Point(target, y), 7, 7);
                }
            }
            else
            {
                var current = Math.Clamp(item.Current, 0, 1) * width;
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xAA, item.Color.R, item.Color.G, item.Color.B)), null, new Rect(left, y - 3, Math.Max(3, current), 6), 3, 3);
                if (Math.Abs(item.Target - item.Current) > 0.001)
                {
                    var target = left + (Math.Clamp(item.Target, 0, 1) * width);
                    context.DrawRoundedRectangle(Brushes.White, null, new Rect(target - 1.5, y - 9, 3, 18), 1.5, 1.5);
                }
            }
        }
    }
}

public sealed class FrequencyMapControl : FrameworkElement
{
    public IReadOnlyList<(MixRole Role, string Name)> Players { get; set; } = [];

    private static (double Low, double High) RangeOf(MixRole role) => role switch
    {
        MixRole.Drums => (3000, 16000),
        MixRole.Bass or MixRole.AudioBass => (40, 250),
        MixRole.Guitar or MixRole.AudioGuitar => (100, 5000),
        MixRole.Lead => (200, 4000),
        MixRole.Keys => (130, 6000),
        _ => (200, 4000),
    };

    private static double X(double hz, double left, double width) => left + (Math.Log10(hz / 20.0) / Math.Log10(1000) * width);

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 500 : availableSize.Width, 40 + (Players.Count * 30));

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        const double left = 14;
        var width = Math.Max(10, ActualWidth - 28);
        var rows = Players.Count;
        var crowd = Enumerable.Range(0, 400)
            .Select(i => 20 * Math.Pow(1000, i / 399.0))
            .Where(hz => Players.Count(p => RangeOf(p.Role).Low <= hz && hz <= RangeOf(p.Role).High && p.Role != MixRole.Drums) >= 3)
            .ToList();
        if (crowd.Count > 0)
        {
            var from = X(crowd.Min(), left, width);
            var to = X(crowd.Max(), left, width);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0xE3, 0x6B, 0x6B)), null, new Rect(from, 22, to - from, rows * 30));
            var crowded = new FormattedText("Crowded: three instruments overlap here", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(0xE3, 0x6B, 0x6B)), dpi);
            context.DrawText(crowded, new Point(Math.Min(from + 4, ActualWidth - crowded.Width - 14), 4));
        }

        for (var i = 0; i < rows; i++)
        {
            var (low, high) = RangeOf(Players[i].Role);
            var a = X(low, left, width);
            var b = X(high, left, width);
            var color = MixBarsControl.RoleColor(Players[i].Role);
            var rect = new Rect(a, 26 + (i * 30), b - a, 22);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, color.R, color.G, color.B)), null, rect, 4, 4);
            var label = new FormattedText(Players[i].Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x0F)), dpi);
            context.DrawText(label, new Point(a + 8, rect.Y + ((22 - label.Height) / 2)));
        }

        foreach (var (text, x) in new[] { ("Low (rumble)", left), ("Middle", left + (width / 2) - 20), ("High (air)", left + width - 70) })
        {
            var f = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
            context.DrawText(f, new Point(x, ActualHeight - 18));
        }
    }
}

public sealed class CompressorCurveControl : FrameworkElement
{
    public double Threshold { get; set; } = -18;

    public double Ratio { get; set; } = 4;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 300 : Math.Min(availableSize.Width, 300), 150);

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        var size = Math.Min(ActualWidth, ActualHeight) - 36;
        var origin = new Point(26, ActualHeight - 22);
        Point At(double input, double output) => new(origin.X + ((input + 40) / 40 * size), origin.Y - ((output + 40) / 40 * size));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)), 1), At(-40, -40), At(0, 0));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(-40, -40), false, false);
            g.LineTo(At(Threshold, Threshold), true, false);
            g.LineTo(At(0, Threshold + (-Threshold / Math.Max(1, Ratio))), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(Ui.AccentBrush, 2.5), geometry);
        context.DrawEllipse(Ui.AccentBrush, null, At(Threshold, Threshold), 4, 4);
        var inLabel = new FormattedText("Input level", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
        context.DrawText(inLabel, new Point(origin.X + (size / 2) - (inLabel.Width / 2), ActualHeight - 18));
        var thLabel = new FormattedText($"Threshold {Threshold:0} dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
        context.DrawText(thLabel, new Point(At(Threshold, Threshold).X + 6, At(Threshold, Threshold).Y + 4));
    }
}
