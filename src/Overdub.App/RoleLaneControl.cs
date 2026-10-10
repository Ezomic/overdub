using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class RoleLaneControl : FrameworkElement
{
    private static readonly string[] StringNames = ["G", "D", "A", "E"];

    public static Color RoleColor(NoteRole role) => role switch
    {
        NoteRole.Root => Color.FromRgb(0x58, 0xB3, 0x6A),
        NoteRole.Third => Color.FromRgb(0xB4, 0x8B, 0xE3),
        NoteRole.Fifth => Color.FromRgb(0x6B, 0x8D, 0xE3),
        NoteRole.Seventh => Color.FromRgb(0xE3, 0x6B, 0x8D),
        NoteRole.Octave => Color.FromRgb(0x4C, 0xC3, 0xA0),
        _ => Color.FromRgb(0xE6, 0xA2, 0x3C),
    };

    public IReadOnlyList<LineNote> Notes { get; set; } = [];

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var line = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33));
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        const double left = 40;
        var right = ActualWidth - 10;
        var gap = (ActualHeight - 40) / 3.0;
        for (var s = 0; s < 4; s++)
        {
            var y = 20 + (s * gap);
            context.DrawLine(new Pen(line, 1), new Point(left - 6, y), new Point(right, y));
            var label = new FormattedText(StringNames[s], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, dim, dpi);
            context.DrawText(label, new Point(14, y - (label.Height / 2)));
        }

        var width = right - left;
        var ink = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x0F));
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        foreach (var note in Notes)
        {
            var (str, _) = BassTab.Position(note.Pitch);
            var y = 20 + (str * gap);
            var text = new FormattedText($"{note.Name} · {LineAnalysis.RoleLabel(note.Role)}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold, 12, ink, dpi);
            var x = left + (note.Start * width);
            var boxWidth = Math.Max(text.Width + 16, (note.Length * width) - 3);
            boxWidth = Math.Min(boxWidth, Math.Max(text.Width + 16, right - x));
            context.DrawRoundedRectangle(new SolidColorBrush(RoleColor(note.Role)), null, new Rect(x, y - 12, boxWidth, 24), 5, 5);
            context.DrawText(text, new Point(x + 8, y - (text.Height / 2)));
        }
    }
}
