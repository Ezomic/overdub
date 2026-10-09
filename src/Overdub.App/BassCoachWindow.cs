using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class BassCoachWindow : Window
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));

    private readonly MainViewModel _main;
    private readonly Track _track;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _chords = new() { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _ideas = new();
    private ChordPattern? _current;

    public BassCoachWindow(MainViewModel main, Window owner, Track track)
    {
        _main = main;
        _track = track;
        Owner = owner;
        Icon = owner.Icon;
        Title = $"Bass coach: {track.Name}";
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var tips = new TextBlock
        {
            Text = "Each card is a bass line over your chords, written as tab (four strings, fret numbers, x is a muted ghost note). Place one on the timeline, loop it with the Loop button, and slow it down with the Speed button until it is clean, then speed it up. Start with the cards near the top and work down.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var head = new StackPanel { Margin = new Thickness(18, 18, 18, 8) };
        head.Children.Add(tips);
        head.Children.Add(_chips);
        head.Children.Add(_chords);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _ideas, Padding = new Thickness(18, 0, 18, 18) };
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(scroll);
        Content = root;
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

    private void Refresh()
    {
        if (!_main.HasTrack(_track))
        {
            Close();
            return;
        }

        _current = _track.ChordPatterns.FirstOrDefault(p => p.Id == _current?.Id) ?? _track.ChordPatterns.FirstOrDefault();
        _chips.Children.Clear();
        foreach (var pattern in _track.ChordPatterns)
        {
            var target = pattern;
            _chips.Children.Add(MakeButton(pattern.Name, () =>
            {
                _current = target;
                Refresh();
            }, pattern == _current, 36));
        }

        _ideas.Children.Clear();
        if (_current is null)
        {
            return;
        }

        var effective = _main.EffectivePattern(_current);
        var source = _current.FollowId is null ? "your own chords for this pattern (change them in the Chords window, or make this bass follow the guitar there)" : "the chords from your guitar machine";
        _chords.Foreground = (Brush)FindResource("TextDim");
        _chords.Text = $"Using {source}:\n" + string.Join("\n", Enumerable.Range(0, effective.Bars).Select(b => $"Bar {b + 1}   {effective[b].Name}:  {string.Join("  ", effective[b].Intervals.Select(i => NoteSpelling.Name(effective[b].Root + i)))}"));

        foreach (var style in ChordPattern.BassStyles)
        {
            var idea = effective.Clone();
            idea.Style = style;
            var card = new Border
            {
                Background = (Brush)FindResource("Panel"),
                BorderBrush = (Brush)FindResource("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = ChordPattern.StyleName(style), FontSize = 15, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = ChordPattern.StyleDescription(style), Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });
            body.Children.Add(new TextBox
            {
                Text = BassTab.Render(idea),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = (Brush)FindResource("Well"),
                Foreground = (Brush)FindResource("Text"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(8),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(MakeButton("Place at playhead", () => _main.PlaceBassIdea(_track, effective, style), width: 130));
            buttons.Children.Add(MakeButton(_current.Style == style ? "Used by this pattern" : "Use in this pattern", () => _main.EditChordPattern(_current, p => p.Style = style, "Change style"), _current.Style == style, 150));
            body.Children.Add(buttons);
            card.Child = body;
            _ideas.Children.Add(card);
        }
    }
}
