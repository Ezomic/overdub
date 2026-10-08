using System.Windows;
using System.Windows.Media;

namespace Overdub.App;

public readonly record struct NoteRect(double X, double Y, double Width, double Height);

public sealed class RectsControl : FrameworkElement
{
    public static readonly DependencyProperty RectsProperty = DependencyProperty.Register(
        nameof(Rects), typeof(object), typeof(RectsControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(RectsControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public object? Rects
    {
        get => GetValue(RectsProperty);
        set => SetValue(RectsProperty, value);
    }

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        if (Rects is not NoteRect[] rects)
        {
            return;
        }

        foreach (var rect in rects)
        {
            context.DrawRoundedRectangle(Brush, null, new Rect(rect.X, rect.Y, rect.Width, rect.Height), 1, 1);
        }
    }
}
