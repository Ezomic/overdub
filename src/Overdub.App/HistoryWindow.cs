using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Overdub.App;

public sealed class HistoryWindow : Window
{
    private readonly MainViewModel _main;
    private readonly StackPanel _list = new();

    public HistoryWindow(MainViewModel main, Window owner)
    {
        _main = main;
        Owner = owner;
        Icon = owner.Icon;
        Title = "Edit history";
        Width = 380;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var hint = new TextBlock
        {
            Text = "Everything you did, oldest at the top. Click a step to go back to just after it. Click a grey step below the line to redo up to it. Making a new edit clears the grey steps.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 10),
        };
        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(16, 0, 16, 16) });
        Content = root;
        _main.EditHistoryChanged += Refresh;
        Closed += (_, _) => _main.EditHistoryChanged -= Refresh;
        Refresh();
    }

    private Button Row(string text, Brush foreground, Action click, bool current = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("TransportButton"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = double.NaN,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = foreground,
            Focusable = false,
        };
        if (current)
        {
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));
        }

        button.Click += (_, _) => click();
        return button;
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var (done, undone) = _main.HistoryNames;
        if (done.Count == 0 && undone.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "Nothing to undo yet.", Foreground = (Brush)FindResource("TextDim") });
            return;
        }

        for (var i = 0; i < done.Count; i++)
        {
            var steps = done.Count - 1 - i;
            var last = i == done.Count - 1;
            _list.Children.Add(Row((last ? "▶ " : "   ") + done[i], (Brush)FindResource("Text"), () => _main.JumpHistory(steps, 0), last));
        }

        for (var j = 0; j < undone.Count; j++)
        {
            var steps = j + 1;
            _list.Children.Add(Row("   " + undone[j], (Brush)FindResource("TextDim"), () => _main.JumpHistory(0, steps)));
        }
    }
}
