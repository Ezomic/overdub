using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class BarSlider : FrameworkElement
{
    private bool _dragging;

    public BarSlider()
    {
        Height = 18;
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    public double Value { get; private set; }

    public event Action<double>? ValueChanged;

    public void SetValue(double value)
    {
        Value = Math.Clamp(value, 0, 1);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        Update(e.GetPosition(this).X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Update(e.GetPosition(this).X);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void Update(double x)
    {
        Value = Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1);
        InvalidateVisual();
        ValueChanged?.Invoke(Value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var track = TryFindResource("Well") as Brush ?? Brushes.Black;
        var y = (ActualHeight - 6) / 2;
        context.DrawRoundedRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight), 0, 0);
        context.DrawRoundedRectangle(track, null, new Rect(0, y, ActualWidth, 6), 3, 3);
        var fill = Math.Max(6, Value * ActualWidth);
        context.DrawRoundedRectangle(Ui.AccentBrush, null, new Rect(0, y, fill, 6), 3, 3);
        context.DrawEllipse(Ui.AccentBrush, new Pen(track, 2), new Point(Math.Clamp(Value * ActualWidth, 7, ActualWidth - 7), ActualHeight / 2), 7, 7);
    }
}

public sealed class WaveformEditorControl : FrameworkElement
{
    private bool _drawing;
    private int _last = -1;

    public WaveformEditorControl()
    {
        Cursor = Cursors.Pen;
    }

    public float[] Wave { get; set; } = DesignedSound.Shape(0);

    public event Action? Drawn;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drawing = true;
        _last = -1;
        CaptureMouse();
        Paint(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drawing)
        {
            Paint(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drawing = false;
        ReleaseMouseCapture();
    }

    private void Paint(Point point)
    {
        var index = Math.Clamp((int)(point.X / Math.Max(1, ActualWidth) * Wave.Length), 0, Wave.Length - 1);
        var value = (float)Math.Clamp(1 - (2 * point.Y / Math.Max(1, ActualHeight)), -1, 1);
        if (_last < 0)
        {
            Wave[index] = value;
        }
        else
        {
            var from = Math.Min(_last, index);
            var to = Math.Max(_last, index);
            var start = Wave[_last];
            for (var i = from; i <= to; i++)
            {
                var t = to == from ? 1f : (float)(i - _last) / (index - _last);
                Wave[i] = start + ((value - start) * t);
            }
        }

        _last = index;
        InvalidateVisual();
        Drawn?.Invoke();
    }

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33)), 1);
        context.DrawLine(grid, new Point(0, ActualHeight / 2), new Point(ActualWidth, ActualHeight / 2));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x29)), 1), new Point(ActualWidth / 2, 0), new Point(ActualWidth / 2, ActualHeight));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < Wave.Length; i++)
            {
                var point = new Point(i * ActualWidth / (Wave.Length - 1), ((1 - Math.Clamp(Wave[i], -1, 1)) / 2 * (ActualHeight - 16)) + 8);
                if (i == 0)
                {
                    g.BeginFigure(point, false, false);
                }
                else
                {
                    g.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(Ui.AccentBrush, 2.5) { LineJoin = PenLineJoin.Round }, geometry);
    }
}

public sealed class EnvelopeEditorControl : FrameworkElement
{
    private const double Pad = 12;
    private int _dragging = -1;

    public EnvelopeEditorControl()
    {
        Cursor = Cursors.Hand;
    }

    public float AttackN { get; set; }
    public float DecayN { get; set; }
    public float Sustain { get; set; }
    public float ReleaseN { get; set; }

    public event Action? Changed;

    private double Span => Math.Max(1, ActualWidth);

    private double Top => 12;

    private double Bottom => Math.Max(Top + 1, ActualHeight - 8);

    private Point[] Points()
    {
        var x0 = Pad;
        var x4 = Span - Pad;
        var x1 = x0 + (0.02 * Span) + (AttackN * 0.3 * Span);
        var x2 = x1 + (0.03 * Span) + (DecayN * 0.3 * Span);
        var x3 = Math.Max(x2 + 8, x4 - (0.03 * Span) - (ReleaseN * 0.3 * Span));
        var sustainY = Bottom - (Sustain * (Bottom - Top));
        return [new Point(x0, Bottom), new Point(x1, Top), new Point(x2, sustainY), new Point(x3, sustainY), new Point(x4, Bottom)];
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var points = Points();
        var best = -1;
        var bestDistance = 14.0;
        for (var i = 1; i <= 3; i++)
        {
            var d = (p - points[i]).Length;
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }

        _dragging = best;
        if (best >= 0)
        {
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging < 0)
        {
            return;
        }

        var p = e.GetPosition(this);
        var x0 = Pad;
        var x4 = Span - Pad;
        var points = Points();
        switch (_dragging)
        {
            case 1:
                AttackN = (float)Math.Clamp((p.X - x0 - (0.02 * Span)) / (0.3 * Span), 0, 1);
                break;
            case 2:
                DecayN = (float)Math.Clamp((p.X - points[1].X - (0.03 * Span)) / (0.3 * Span), 0, 1);
                Sustain = (float)Math.Clamp((Bottom - p.Y) / (Bottom - Top), 0, 1);
                break;
            default:
                ReleaseN = (float)Math.Clamp((x4 - (0.03 * Span) - p.X) / (0.3 * Span), 0, 1);
                break;
        }

        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = -1;
        ReleaseMouseCapture();
    }

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        var points = Points();
        var good = new SolidColorBrush(Color.FromRgb(0x5E, 0xC6, 0xA0));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Length; i++)
            {
                g.LineTo(points[i], true, false);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(good, 2.5) { LineJoin = PenLineJoin.Round }, geometry);
        for (var i = 1; i <= 3; i++)
        {
            context.DrawEllipse(good, null, points[i], 5, 5);
        }
    }
}
