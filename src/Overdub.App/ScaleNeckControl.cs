using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class ScaleNeckControl : FrameworkElement
{
    private static readonly int[] OpenStrings = [7, 2, 9, 4];
    private static readonly string[] StringNames = ["G", "D", "A", "E"];
    private static readonly int[] Markers = [3, 5, 7, 9, 12];
    private const int Frets = 12;

    public IReadOnlyList<ScaleNote> Notes { get; set; } = [];

    public static Color RoleColor(ScaleRole role) => role switch
    {
        ScaleRole.Root => Color.FromRgb(0x58, 0xB3, 0x6A),
        ScaleRole.ChordTone => Color.FromRgb(0x6B, 0x8D, 0xE3),
        ScaleRole.Colour => Color.FromRgb(0xB4, 0x8B, 0xE3),
        _ => Color.FromRgb(0xE6, 0xA2, 0x3C),
    };

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        var left = 58.0;
        var right = ActualWidth - 10;
        var fretWidth = (right - left) / Frets;
        var top = 22.0;
        var gap = 32.0;
        var bottom = top + (3 * gap);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33)), 1);
        var nut = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 3);
        for (var f = 0; f <= Frets; f++)
        {
            var x = left + (f * fretWidth);
            context.DrawLine(f == 0 ? nut : grid, new Point(x, top - 6), new Point(x, bottom + 6));
            if (Markers.Contains(f))
            {
                var label = Text(f.ToString(CultureInfo.InvariantCulture), dim, 12, dpi, false);
                context.DrawText(label, new Point(left + ((f - 0.5) * fretWidth) - (label.Width / 2), bottom + 12));
            }
        }

        var byPitchClass = Notes.ToDictionary(n => n.PitchClass);
        var ink = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x0F));
        var stringPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)), 1);
        for (var s = 0; s < 4; s++)
        {
            var y = top + (s * gap);
            context.DrawLine(stringPen, new Point(left, y), new Point(right, y));
            var name = Text(StringNames[s], dim, 12, dpi, false);
            context.DrawText(name, new Point(10, y - (name.Height / 2)));
            for (var f = 0; f <= Frets; f++)
            {
                if (!byPitchClass.TryGetValue((OpenStrings[s] + f) % 12, out var note))
                {
                    continue;
                }

                var cx = f == 0 ? left - 14 : left + ((f - 0.5) * fretWidth);
                context.DrawEllipse(new SolidColorBrush(RoleColor(note.Role)), null, new Point(cx, y), 11, 11);
                var text = Text(note.Name, ink, 12, dpi, true);
                context.DrawText(text, new Point(cx - (text.Width / 2), y - (text.Height / 2)));
            }
        }
    }

    private static FormattedText Text(string value, Brush brush, double size, double dpi, bool bold) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, dpi);
}
