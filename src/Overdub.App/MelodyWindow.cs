using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class MelodyWindow : Window
{
    private static readonly int[] BarOptions = [1, 2, 4, 8];

    private readonly MainViewModel _main;
    private readonly Track _track;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
    private int _key;
    private MelodyScale _scale = MelodyScale.MinorPentatonic;
    private int _bars = 4;
    private MelodyDensity _density = MelodyDensity.Medium;

    public MelodyWindow(MainViewModel main, Window owner, Track track)
    {
        _main = main;
        _track = track;
        Owner = owner;
        Icon = owner.Icon;
        Title = track.Name;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var hint = new TextBlock
        {
            Text = "Pick a key and scale, then Generate to drop a single-note melody at the playhead. Each press writes a new one, and undo removes it. To build on it, double-click the block to edit its notes in the piano roll, or arm this track (R) and play along on your MIDI keyboard to record more.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(_row);
        panel.Children.Add(hint);
        Content = panel;
        Refresh();
    }

    private Button MenuButton(string text, IEnumerable<(string Label, Action Pick)> items, double width)
    {
        var button = Button(text, () => { }, width);
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (label, pick) in items)
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) =>
                {
                    pick();
                    Refresh();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        return button;
    }

    private Button Button(string text, Action click, double width)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("TransportButton"),
            Width = width,
            Height = 26,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Focusable = false,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void Refresh()
    {
        _row.Children.Clear();
        _row.Children.Add(MenuButton($"Key of {Chord.Roots[_key]}", Enumerable.Range(0, 12).Select(r => (Chord.Roots[r], (Action)(() => _key = r))), 100));
        _row.Children.Add(MenuButton(MelodyGenerator.ScaleName(_scale), Enum.GetValues<MelodyScale>().Select(s => (MelodyGenerator.ScaleName(s), (Action)(() => _scale = s))), 130));
        _row.Children.Add(MenuButton(_bars == 1 ? "1 bar" : $"{_bars} bars", BarOptions.Select(b => (b == 1 ? "1 bar" : $"{b} bars", (Action)(() => _bars = b))), 80));
        _row.Children.Add(MenuButton(_density.ToString(), Enum.GetValues<MelodyDensity>().Select(d => (d.ToString(), (Action)(() => _density = d))), 80));
        _row.Children.Add(Button("Generate at playhead", () => _main.GenerateMelody(_track, _key, _scale, _bars, _density), 160));
    }
}
