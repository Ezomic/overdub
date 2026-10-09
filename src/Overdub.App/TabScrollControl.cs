using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed record TabNote(long Start, long End, int String, int Fret);

public sealed record TabBar(long Start, string Chord);

public sealed class TabScrollControl : FrameworkElement
{
    private static readonly string[] StringNames = ["G", "D", "A", "E"];
    private static readonly int[] OpenPitches = [43, 38, 33, 28];
    public static readonly string[] LabelModeNames = ["Fret numbers", "Note names", "Both"];
    private const double PixelsPerSecond = 110;
    private const double NowX = 170;

    public IReadOnlyList<TabNote> Notes { get; set; } = [];
    public IReadOnlyList<TabBar> Bars { get; set; } = [];
    public long Position { get; set; }
    public int SampleRate { get; set; } = 44100;
    public bool Playing { get; set; }
    public int LabelMode { get; set; } = 2;

    public static string NoteName(int stringIndex, int fret) => Chord.Roots[(OpenPitches[stringIndex] + fret) % 12];

    private string Label(TabNote note) => LabelMode switch
    {
        0 => note.Fret.ToString(CultureInfo.InvariantCulture),
        1 => NoteName(note.String, note.Fret),
        _ => $"{note.Fret.ToString(CultureInfo.InvariantCulture)} {NoteName(note.String, note.Fret)}",
    };

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var line = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var text = TryFindResource("Text") as Brush ?? Brushes.White;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double left = 30;
        const double top = 38;
        var gap = (ActualHeight - top - 16) / 3.0;
        context.DrawRectangle(well, null, new Rect(left, top - 14, ActualWidth - left - 8, (gap * 3) + 28));
        for (var s = 0; s < 4; s++)
        {
            var y = top + (s * gap);
            context.DrawLine(new Pen(line, 1), new Point(left, y), new Point(ActualWidth - 8, y));
            var name = new FormattedText(StringNames[s], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
            context.DrawText(name, new Point(10, y - (name.Height / 2)));
        }

        double X(long sample) => NowX + ((sample - Position) / (double)SampleRate * PixelsPerSecond);
        foreach (var bar in Bars)
        {
            var x = X(bar.Start);
            if (x < left - 40 || x > ActualWidth)
            {
                continue;
            }

            context.DrawLine(new Pen(line, 1.2), new Point(x, top - 14), new Point(x, top + (gap * 3) + 14));
            var label = new FormattedText(bar.Chord, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, text, dpi);
            context.DrawText(label, new Point(x + 5, 4));
        }

        var accent = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));
        context.PushClip(new RectangleGeometry(new Rect(left, 0, Math.Max(0, ActualWidth - left - 8), ActualHeight)));
        foreach (var note in Notes)
        {
            var x = X(note.Start);
            var end = X(note.End);
            if (end < left - 20 || x > ActualWidth + 20)
            {
                continue;
            }

            var y = top + (note.String * gap);
            var past = end < NowX;
            var current = x <= NowX && end >= NowX && Playing;
            var fill = current ? accent : past ? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4C)) : new SolidColorBrush(Color.FromRgb(0x58, 0xB3, 0x6A));
            var number = new FormattedText(Label(note), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 12, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), dpi);
            var width = Math.Max(number.Width + 14, end - x - 2);
            context.DrawRoundedRectangle(fill, null, new Rect(x, y - 10, width, 20), 4, 4);
            context.DrawText(number, new Point(x + 7, y - (number.Height / 2)));
        }

        context.Pop();
        context.DrawLine(new Pen(Playing ? accent : Brushes.White, 2), new Point(NowX - 3, top - 18), new Point(NowX - 3, top + (gap * 3) + 18));
    }
}
