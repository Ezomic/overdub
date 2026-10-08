using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class ChordWindow : Window
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));

    private static readonly (string Name, int[] Degrees)[] Progressions =
    [
        ("I V vi IV", [1, 5, 6, 4]),
        ("I IV V I", [1, 4, 5, 1]),
        ("vi IV I V", [6, 4, 1, 5]),
        ("I vi IV V", [1, 6, 4, 5]),
        ("ii V I I", [2, 5, 1, 1]),
    ];

    private readonly MainViewModel _main;
    private readonly Track _track;
    private readonly StackPanel _patternBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _chordRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
    private readonly StackPanel _quickRow = new() { Orientation = Orientation.Horizontal };
    private ChordPattern? _current;
    private int _keyRoot;
    private bool _minorKey;

    public ChordWindow(MainViewModel main, Window owner, Track track)
    {
        _main = main;
        _track = track;
        Owner = owner;
        Icon = owner.Icon;
        Title = $"{track.Name}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var hint = new TextBlock
        {
            Text = "Pick a chord for each bar, or fill them from a key and a common progression. Place puts the pattern at the playhead as a block you can move, copy and delete on the timeline. Change the sound with the preset button on the track.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(_patternBar);
        panel.Children.Add(_chordRow);
        panel.Children.Add(new TextBlock { Text = "Quick fill", Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(_quickRow);
        panel.Children.Add(hint);
        Content = panel;
        _main.EditHistoryChanged += Refresh;
        Closed += (_, _) => _main.EditHistoryChanged -= Refresh;
        Refresh();
    }

    private Button MakeButton(string text, Action click, bool selected = false, double width = double.NaN)
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
        if (selected)
        {
            button.BorderBrush = Accent;
            button.Foreground = Accent;
        }

        button.Click += (_, _) => click();
        return button;
    }

    private Button MenuButton(string text, IEnumerable<(string Label, Action Pick)> items, double width)
    {
        var button = MakeButton(text, () => { }, width: width);
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (label, pick) in items)
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => pick();
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        return button;
    }

    private void Refresh()
    {
        if (!_main.HasTrack(_track))
        {
            Close();
            return;
        }

        _current = _track.ChordPatterns.FirstOrDefault(p => p.Id == _current?.Id) ?? _track.ChordPatterns.FirstOrDefault();
        _patternBar.Children.Clear();
        foreach (var pattern in _track.ChordPatterns)
        {
            var target = pattern;
            _patternBar.Children.Add(MakeButton(pattern.Name, () =>
            {
                _current = target;
                Refresh();
            }, pattern == _current, 36));
        }

        if (_track.ChordPatterns.Count < 8)
        {
            _patternBar.Children.Add(MakeButton("+", () =>
            {
                _current = _main.AddChordPattern(_track);
                Refresh();
            }, width: 30));
        }

        _chordRow.Children.Clear();
        _quickRow.Children.Clear();
        if (_current is not { } current)
        {
            return;
        }

        _patternBar.Children.Add(new Border { Width = 16 });
        _patternBar.Children.Add(MakeButton(current.Bars == 1 ? "1 bar" : $"{current.Bars} bars", () => _main.EditChordPattern(current, p => p.Bars = (p.Bars % ChordPattern.MaxBars) + 1, "Change pattern length"), width: 70));
        var styles = ChordPattern.Styles(current.Role);
        _patternBar.Children.Add(MenuButton(ChordPattern.StyleName(current.Style), styles.Select(s => (ChordPattern.StyleName(s), (Action)(() => _main.EditChordPattern(current, p => p.Style = s, "Change style")))), 130));
        _patternBar.Children.Add(MakeButton("Place at playhead", () => _main.PlaceChordPattern(_track, current), width: 130));

        for (var bar = 0; bar < current.Bars; bar++)
        {
            var index = bar;
            var chord = current[bar];
            var column = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            column.Children.Add(new TextBlock { Text = $"Bar {bar + 1}", Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
            column.Children.Add(MenuButton(Chord.Roots[chord.Root], Enumerable.Range(0, 12).Select(r => (Chord.Roots[r], (Action)(() => _main.EditChordPattern(current, p => p[index] = p[index] with { Root = r }, "Change chord")))), 70));
            column.Children.Add(MenuButton(Chord.QualityNames[(int)chord.Quality], Enumerable.Range(0, Chord.QualityNames.Length).Select(q => (Chord.QualityNames[q], (Action)(() => _main.EditChordPattern(current, p => p[index] = p[index] with { Quality = (ChordQuality)q }, "Change chord")))), 70));
            _chordRow.Children.Add(column);
        }

        _quickRow.Children.Add(MenuButton($"Key of {Chord.Roots[_keyRoot]}", Enumerable.Range(0, 12).Select(r => (Chord.Roots[r], (Action)(() =>
        {
            _keyRoot = r;
            Refresh();
        }))), 100));
        _quickRow.Children.Add(MakeButton(_minorKey ? "minor" : "major", () =>
        {
            _minorKey = !_minorKey;
            Refresh();
        }, width: 70));
        foreach (var (name, degrees) in Progressions)
        {
            var degreeSet = degrees;
            _quickRow.Children.Add(MakeButton(_minorKey ? name.ToLowerInvariant() : name, () => _main.EditChordPattern(current, p => p.ApplyProgression(_keyRoot, _minorKey, degreeSet), "Fill chords")));
        }
    }
}
