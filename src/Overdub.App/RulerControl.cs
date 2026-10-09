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

    public static readonly DependencyProperty SectionsProperty = DependencyProperty.Register(
        nameof(Sections), typeof(IReadOnlyList<SectionBand>), typeof(RulerControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color[] BandColors =
    [
        Color.FromRgb(0x4C, 0x9A, 0xFF), Color.FromRgb(0x5B, 0xC0, 0x70), Color.FromRgb(0x9B, 0x8A, 0xFB), Color.FromRgb(0xE5, 0xA3, 0x3B), Color.FromRgb(0xE5, 0x61, 0x9B), Color.FromRgb(0x2E, 0xC4, 0xB6),
    ];

    public IReadOnlyList<SectionBand>? Sections
    {
        get => (IReadOnlyList<SectionBand>?)GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

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
        foreach (var band in Sections ?? [])
        {
            var color = BandColors[band.Index % BandColors.Length];
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)), null, new Rect(band.Left, 0, band.Width, ActualHeight));
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(band.Left, 0, 2, ActualHeight));
            var bandLabel = new FormattedText(band.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 10.5, new SolidColorBrush(Color.FromRgb(0xEC, 0xEB, 0xEE)), VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(10, band.Width - 28), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(bandLabel, new Point(band.Left + 26, 3));
        }

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
