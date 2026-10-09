using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class FretboardControl : FrameworkElement
{
    private static readonly int[] OpenStrings = [43, 38, 33, 28];
    private static readonly string[] StringNames = ["G", "D", "A", "E"];
    private static readonly int[] Inlays = [3, 5, 7, 9, 12, 15];

    private static readonly Color Root = Color.FromRgb(0xE0, 0x9F, 0x3E);
    private static readonly Color Third = Color.FromRgb(0x58, 0xB3, 0x6A);
    private static readonly Color Fifth = Color.FromRgb(0x4C, 0x9A, 0xFF);
    private static readonly Color Seventh = Color.FromRgb(0xB0, 0x8A, 0xFB);

    public const int Frets = 15;

    public Chord? Chord { get; set; }
    public (int Root, MelodyScale Scale)? Key { get; set; }
    public bool ShowScale { get; set; } = true;
    public int? PlayingPitch { get; set; }

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var line = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var text = TryFindResource("Text") as Brush ?? Brushes.White;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double left = 34;
        const double top = 28;
        var width = ActualWidth - left - 12;
        var fretWidth = width / (Frets + 1);
        var stringGap = (ActualHeight - top - 14) / 3.0;
        context.DrawRectangle(well, null, new Rect(left, top - 10, width, (stringGap * 3) + 20));

        for (var fret = 0; fret <= Frets; fret++)
        {
            var x = left + (fret * fretWidth);
            if (fret > 0)
            {
                context.DrawLine(new Pen(line, fret == 1 ? 1 : 1), new Point(x, top - 10), new Point(x, top + (stringGap * 3) + 10));
            }

            var number = new FormattedText(fret.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, dim, dpi);
            context.DrawText(number, new Point(x + (fretWidth / 2) - (number.Width / 2), 2));
            if (Inlays.Contains(fret))
            {
                var cy = top + (stringGap * 1.5);
                context.DrawEllipse(line, null, new Point(x + (fretWidth / 2), cy), 4, 4);
            }
        }

        var scale = Key is { } k ? MelodyGenerator.ScaleOffsets(k.Scale) : null;
        for (var s = 0; s < 4; s++)
        {
            var y = top + (s * stringGap);
            context.DrawLine(new Pen(line, 1.5 + ((3 - s) * 0.4)), new Point(left, y), new Point(left + width, y));
            var name = new FormattedText(StringNames[s], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
            context.DrawText(name, new Point(12, y - (name.Height / 2)));
            for (var fret = 0; fret <= Frets; fret++)
            {
                var pitch = OpenStrings[s] + fret;
                var pc = pitch % 12;
                var cx = left + (fret * fretWidth) + (fretWidth / 2);
                var role = RoleOf(pc);
                if (role is { } r)
                {
                    DrawNote(context, new Point(cx, y), r.Color, pc, r.Label, text, dpi, pitch == PlayingPitch);
                }
                else if (ShowScale && scale is not null && Key is { } key && scale.Contains((((pc - key.Root) % 12) + 12) % 12))
                {
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 0x9A, 0x9A, 0xA2)), null, new Point(cx, y), 5, 5);
                }
            }
        }
    }

    private (Color Color, string Label)? RoleOf(int pitchClass)
    {
        if (Chord is not { } chord)
        {
            return null;
        }

        var interval = (((pitchClass - chord.Root) % 12) + 12) % 12;
        if (!chord.Intervals.Contains(interval))
        {
            return null;
        }

        return interval switch
        {
            0 => (Root, "R"),
            3 or 4 or 5 => (Third, interval == 5 ? "4" : "3"),
            6 or 7 or 8 => (Fifth, "5"),
            _ => (Seventh, "7"),
        };
    }

    private static void DrawNote(DrawingContext context, Point center, Color color, int pitchClass, string role, Brush text, double dpi, bool playing)
    {
        if (playing)
        {
            context.DrawEllipse(null, new Pen(Brushes.White, 2.5), center, 14, 14);
        }

        context.DrawEllipse(new SolidColorBrush(color), null, center, 11, 11);
        var label = new FormattedText(role, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 11, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), dpi);
        context.DrawText(label, new Point(center.X - (label.Width / 2), center.Y - (label.Height / 2)));
    }
}
