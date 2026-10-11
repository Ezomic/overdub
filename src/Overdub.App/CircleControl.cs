using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class CircleControl : FrameworkElement
{
    private const double Size = 330;
    private const double Outer = 150;
    private const double Inner = 86;
    private const double MajorRadius = 118;
    private const double MinorRadius = 64;

    public CircleControl()
    {
        Width = Size;
        Height = Size;
        Cursor = Cursors.Hand;
    }

    public int Selected { get; set; }

    public bool Minor { get; set; }

    public event Action<int, bool>? KeySelected;

    private static Point Position(int index, double radius)
    {
        var angle = (index * 30 - 90) * Math.PI / 180;
        return new Point((Size / 2) + (radius * Math.Cos(angle)), (Size / 2) + (radius * Math.Sin(angle)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var dx = p.X - (Size / 2);
        var dy = p.Y - (Size / 2);
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance > Outer || distance < 24)
        {
            return;
        }

        var degrees = (Math.Atan2(dy, dx) * 180 / Math.PI) + 90 + 15;
        var index = (int)(((degrees % 360) + 360) % 360 / 30) % 12;
        KeySelected?.Invoke(index, distance <= Inner);
    }

    protected override void OnRender(DrawingContext context)
    {
        var panel = TryFindResource("Panel") as Brush ?? Brushes.Black;
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        var text = TryFindResource("Text") as Brush ?? Brushes.White;
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var centre = new Point(Size / 2, Size / 2);
        context.DrawEllipse(panel, new Pen(border, 1), centre, Outer, Outer);
        context.DrawEllipse(well, new Pen(border, 1), centre, Inner, Inner);

        var (r0, r1) = Minor ? (24.0, Inner) : (Inner, Outer);
        context.DrawGeometry(Ui.AccentTint, null, Wedge(Selected, r0, r1));

        for (var i = 0; i < CircleOfFifths.Count; i++)
        {
            var majorSelected = !Minor && i == Selected;
            Label(context, CircleOfFifths.MajorName(i), Position(i, MajorRadius), majorSelected ? Ui.AccentBrush : text, 15, majorSelected, dpi);
            var minorSelected = Minor && i == Selected;
            Label(context, CircleOfFifths.MinorName(i), Position(i, MinorRadius), minorSelected ? Ui.AccentBrush : dim, 12.5, minorSelected, dpi);
        }

        var ring = Minor ? MinorRadius : MajorRadius;
        context.DrawEllipse(null, new Pen(Ui.AccentBrush, 2), Position(Selected, ring), 18, 18);
        var neighbour = new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x8D, 0xE3)), 2) { DashStyle = new DashStyle([2, 2], 0) };
        foreach (var n in new[] { (Selected + 11) % 12, (Selected + 1) % 12 })
        {
            context.DrawEllipse(null, neighbour, Position(n, ring), 16, 16);
        }
    }

    private static Geometry Wedge(int index, double r0, double r1)
    {
        var a0 = ((index * 30) - 15 - 90) * Math.PI / 180;
        var a1 = ((index * 30) + 15 - 90) * Math.PI / 180;
        Point At(double r, double a) => new((Size / 2) + (r * Math.Cos(a)), (Size / 2) + (r * Math.Sin(a)));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(r0, a0), true, true);
            g.LineTo(At(r1, a0), false, false);
            g.ArcTo(At(r1, a1), new Size(r1, r1), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(At(r0, a1), false, false);
            g.ArcTo(At(r0, a0), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void Label(DrawingContext context, string value, Point at, Brush brush, double size, bool bold, double dpi)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        context.DrawText(formatted, new Point(at.X - (formatted.Width / 2), at.Y - (formatted.Height / 2)));
    }
}
