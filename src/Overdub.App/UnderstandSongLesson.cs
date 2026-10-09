using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public sealed class UnderstandSongLesson : UserControl
{
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string _signature = "";
    private int? _pinned;
    private int _lastBar = -2;

    public UnderstandSongLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        _timer.Tick += (_, _) => Update();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                _signature = "";
                Update();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Loaded += (_, _) => Update();
    }

    private void Update()
    {
        var main = _context.Main;
        var key = main.DetectSongKeyQuick();
        var (chords, bar) = main.ProgressionAtPlayhead();
        if (bar != _lastBar)
        {
            if (bar >= 0)
            {
                _pinned = null;
            }

            _lastBar = bar;
        }

        var signature = $"{key?.Name}|{string.Join(",", chords.Select(c => c.PlainName))}|{bar}|{_pinned}|{NoteSpelling.ShowNumerals}|{NoteSpelling.UseFlats}";
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        Build(key, chords, bar);
    }

    private void Build(SongKey? key, IReadOnlyList<Chord> chords, int playing)
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Understand your song"));
        _page.Children.Add(Ui.Sub("Every chord has a number. The number tells you how it relates to the key, whatever the key is.", 12));
        if (key is null || chords.Count == 0)
        {
            var empty = new DockPanel();
            var go = Ui.Secondary("Build a song", "Start from a template or add players", 120);
            go.Click += (_, _) => _context.Navigate(1);
            DockPanel.SetDock(go, Dock.Right);
            empty.Children.Add(go);
            empty.Children.Add(new TextBlock { Text = "There is no song with chords yet. Start one in Build a song, then come back.", FontSize = 13, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center });
            _page.Children.Add(Ui.Card(empty, 14));
            return;
        }

        var strip = new DockPanel();
        var hearAll = Ui.Secondary("Hear the whole progression", "Play each chord in turn", 190);
        hearAll.Click += async (_, _) => await _context.Main.HearChords(chords);
        DockPanel.SetDock(hearAll, Dock.Right);
        strip.Children.Add(hearAll);
        var pills = new StackPanel { Orientation = Orientation.Horizontal };
        pills.Children.Add(Ui.Pill($"Key: {NoteSpelling.KeyName(key)}"));
        pills.Children.Add(Ui.Pill($"{_context.Main.Bpm:0} BPM"));
        strip.Children.Add(pills);
        _page.Children.Add(Ui.Card(strip, 12));

        var selected = _pinned ?? (playing >= 0 ? playing : 0);
        selected = Math.Clamp(selected, 0, chords.Count - 1);
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 2) };
        for (var i = 0; i < chords.Count; i++)
        {
            tiles.Children.Add(Tile(chords[i], i, key, i == selected, i == playing));
        }

        _page.Children.Add(tiles);
        var chord = chords[selected];
        var info = Theory.Describe(chord, key.Root, key.Minor);
        _page.Children.Add(Explanation(info));
        _page.Children.Add(Actions(chord));
        _page.Children.Add(Ui.Sub("Many songs use I, V, vi, IV in some order. Click any tile to read about it, or press play and watch the highlight follow the music.", 0));
    }

    private UIElement Tile(Chord chord, int index, SongKey key, bool selected, bool playing)
    {
        var info = Theory.Describe(chord, key.Root, key.Minor);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        stack.Children.Add(new TextBlock { Text = info.Numeral, FontFamily = new FontFamily("Georgia"), FontSize = 34, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = info.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        var (back, fore) = ChipColors(info.Function);
        stack.Children.Add(new Border
        {
            Background = back,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 1, 9, 1),
            Margin = new Thickness(0, 6, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = info.FunctionLabel, FontSize = 12, Foreground = fore },
        });
        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(new TextBlock { Text = $"Bar {index + 1}", FontSize = 11.5, Foreground = Ui.Res("TextDim"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, -6, 0, 0) });
        if (playing)
        {
            grid.Children.Add(new TextBlock { Text = "▶ playing", FontSize = 11.5, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -2, 0) });
        }

        var tile = new Border
        {
            Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 8),
            Margin = new Thickness(0, 0, 10, 12),
            Cursor = Cursors.Hand,
            ToolTip = $"{info.Numeral} · {info.Name}: click to read about it",
            Child = grid,
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            _pinned = index;
            _signature = "";
            Update();
        };
        return tile;
    }

    private static (Brush Back, Brush Fore) ChipColors(ChordFunction function) => function switch
    {
        ChordFunction.Home or ChordFunction.SoftHome => (Frozen(0x1F, 0x3A, 0x27), Frozen(0x8F, 0xD6, 0xA0)),
        ChordFunction.Away => (Frozen(0x2A, 0x33, 0x50), Frozen(0x9D, 0xB4, 0xF0)),
        ChordFunction.Tension => (Frozen(0x43, 0x28, 0x1F), Frozen(0xF0, 0xA5, 0x8A)),
        ChordFunction.RelativeMinor or ChordFunction.RelativeMajor => (Frozen(0x3B, 0x2F, 0x4D), Frozen(0xC6, 0xA8, 0xEF)),
        _ => (Frozen(0x3A, 0x3A, 0x42), Frozen(0xD6, 0xD6, 0xDA)),
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private UIElement Explanation(ChordInfo info)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = info.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = info.Explanation, FontSize = 13.5, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 8) });
        var tipText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tipText.Inlines.Add(new System.Windows.Documents.Run("Bass tip: ") { FontWeight = FontWeights.SemiBold });
        tipText.Inlines.Add(new System.Windows.Documents.Run(info.BassTip));
        stack.Children.Add(new Border
        {
            Background = Frozen(0x1B, 0x2A, 0x22),
            BorderBrush = Ui.GoodBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = tipText,
        });
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }

    private UIElement Actions(Chord chord)
    {
        var row = new DockPanel();
        var hear = Ui.Accent("Hear this chord", "Play this chord on your guitar machine's sound");
        hear.Margin = new Thickness(0, 0, 8, 0);
        hear.OnClick(async () => await _context.Main.HearChords([chord]));
        var neck = Ui.Secondary("See it on the bass neck", "Open the bass neck and play along", 170);
        neck.Click += (_, _) => LearnCatalog.OpenPractice(_context);
        var numerals = new CheckBox
        {
            Content = "Show numbers instead of chord names everywhere in the app",
            IsChecked = NoteSpelling.ShowNumerals,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Foreground = Frozen(0xD6, 0xD6, 0xDA),
        };
        numerals.Checked += (_, _) => SetNumerals(true);
        numerals.Unchecked += (_, _) => SetNumerals(false);
        DockPanel.SetDock(numerals, Dock.Right);
        row.Children.Add(numerals);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(hear);
        left.Children.Add(neck);
        row.Children.Add(left);
        return row;
    }

    private void SetNumerals(bool on)
    {
        if (NoteSpelling.ShowNumerals == on)
        {
            return;
        }

        NoteSpelling.ShowNumerals = on;
        _signature = "";
        Dispatcher.BeginInvoke(Update);
    }
}
