using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Overdub.App;

public static class Ui
{
    public static readonly Brush AccentBrush = Frozen(Color.FromRgb(0xE6, 0xA2, 0x3C));
    public static readonly Brush AccentTint = Frozen(Color.FromRgb(0x2B, 0x24, 0x18));
    public static readonly Brush AccentText = Frozen(Color.FromRgb(0x1B, 0x13, 0x06));
    public static readonly Brush Hover = Frozen(Color.FromRgb(0x2B, 0x2A, 0x30));
    public static readonly Brush GoodBrush = Frozen(Color.FromRgb(0x58, 0xB3, 0x6A));
    public static readonly Brush GoodText = Frozen(Color.FromRgb(0x0F, 0x24, 0x14));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock Title(string text) => new() { Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold };

    public static TextBlock Sub(string text, double bottom = 14) => new() { Text = text, FontSize = 13, Foreground = Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, bottom) };

    public static TextBlock Label(string text) => new() { Text = text, FontSize = 13, Foreground = Res("TextDim"), Margin = new Thickness(0, 0, 0, 6) };

    public static Button Secondary(string text, string? tip = null, double width = double.NaN)
    {
        return new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Width = width,
            MinWidth = 40,
            Height = 26,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Focusable = false,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static Border Accent(string text, string? tip = null)
    {
        var label = new TextBlock { Text = text, Foreground = AccentText, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var button = new Border
        {
            Background = AccentBrush,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(18, 6, 18, 6),
            Cursor = Cursors.Hand,
            Child = label,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip,
            Focusable = false,
        };
        return button;
    }

    public static void OnClick(this Border border, Action action)
    {
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
    }

    public static Border Card(UIElement child, double bottom = 8)
    {
        return new Border
        {
            Background = Res("Panel"),
            BorderBrush = Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, bottom),
            Child = child,
        };
    }

    public static Border ClickCard(UIElement child, Action action, string? tip = null)
    {
        var card = Card(child, 0);
        card.Cursor = Cursors.Hand;
        card.ToolTip = tip;
        card.MouseEnter += (_, _) => card.Background = Hover;
        card.MouseLeave += (_, _) => card.Background = Res("Panel");
        card.OnClick(action);
        return card;
    }

    public static Border Pill(string text) => new()
    {
        Background = Hover,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Res("Text") },
    };
}
