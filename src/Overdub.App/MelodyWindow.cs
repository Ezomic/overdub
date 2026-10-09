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
    private readonly WrapPanel _row = new() { Margin = new Thickness(0, 0, 0, 14), MaxWidth = 860 };
    private int _key;
    private MelodyScale _scale = MelodyScale.MinorPentatonic;
    private int _bars = 4;
    private MelodyDensity _density = MelodyDensity.Medium;
    private string? _followId;
    private int _ornaments;

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
            Text = "Pick a key and scale (and, to make the melody fit a chord progression, Follow a guitar or bass pattern, with the key set to the key of that progression), then Generate to drop a single-note melody at the playhead. Each press writes a new one, and undo removes it. To build on it, make variations of a block (click it first), turn a recorded or imported clip of single notes into a block with Notes from audio clip, double-click the block to edit its notes in the piano roll, or arm this track (R) and play along on your MIDI keyboard to record more.",
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
            Margin = new Thickness(0, 0, 6, 6),
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
        _row.Children.Add(MenuButton($"Ornaments: {MelodyGenerator.OrnamentNames[_ornaments].ToLowerInvariant()}", Enumerable.Range(0, MelodyGenerator.OrnamentNames.Length).Select(o => (MelodyGenerator.OrnamentNames[o], (Action)(() => _ornaments = o))), 190));
        var patterns = _main.GuitarPatterns().Concat(_main.BassPatterns()).ToList();
        var followed = patterns.FirstOrDefault(p => p.Pattern.Id == _followId);
        var choices = new List<(string Label, Action Pick)> { ("No chords", () => _followId = null) };
        choices.AddRange(patterns.Select(p => ($"Follow {p.Label}", (Action)(() => _followId = p.Pattern.Id))));
        _row.Children.Add(MenuButton(followed.Pattern is null ? "No chords" : $"Following {followed.Label}", choices, 190));
        _row.Children.Add(Button("Generate at playhead", () => _main.GenerateMelody(_track, _key, _scale, _bars, _density, followed.Pattern, _ornaments), 160));
        _row.Children.Add(Button("Notes from audio clip", async () => await _main.TranscribeAudioAsync(_track), 150));
        _row.Children.Add(Button("Make 4 variations", () => _main.MakeVariations(_track, 4), 140));
    }
}
