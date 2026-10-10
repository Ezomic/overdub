using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public sealed class BassLineLesson : UserControl
{
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string _signature = "";
    private int? _pinned;
    private int _lastBar = -2;

    public BassLineLesson(LearnContext context)
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
        var line = main.BassLineAtPlayhead();
        if (line is not null && line.Playing != _lastBar)
        {
            if (line.Playing >= 0)
            {
                _pinned = null;
            }

            _lastBar = line.Playing;
        }

        var signature = line is null
            ? $"none|{key?.Name}"
            : $"{key?.Name}|{line.Style}|{line.Playing}|{_pinned}|{main.BassIsMuted}|{NoteSpelling.ShowNumerals}|{NoteSpelling.UseFlats}|" + string.Join(";", line.Bars.Select(b => $"{b.Chord.Root}{b.Chord.Quality}:{string.Join(",", b.Notes.Select(n => n.Pitch))}"));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        Build(key, line);
    }

    private void Header()
    {
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Why this bass line works"));
        _page.Children.Add(Ui.Sub("Every note has a job. See what each note of your bass machine's line does, and why it fits the chord.", 12));
    }

    private void Build(SongKey? key, MainViewModel.BassLineInfo? line)
    {
        _page.Children.Clear();
        Header();
        if (key is null || line is null)
        {
            var empty = new DockPanel();
            var go = Ui.Secondary("Build a song", "Add a bass machine and place a bass line", 120);
            go.Click += (_, _) => _context.Navigate(1);
            DockPanel.SetDock(go, Dock.Right);
            empty.Children.Add(go);
            empty.Children.Add(new TextBlock
            {
                Text = _context.Main.Tracks.Any(t => t.Model.Machine == MachineRole.Bass)
                    ? "Your bass machine has no line yet. Place a chord pattern for it in Build a song (Chords), then come back."
                    : "There is no bass machine yet. Add one in Build a song, then come back.",
                FontSize = 13,
                Foreground = Ui.Res("TextDim"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            _page.Children.Add(Ui.Card(empty, 14));
            return;
        }

        var styleStack = new StackPanel();
        styleStack.Children.Add(new TextBlock { Text = $"Style: {ChordPattern.StyleName(line.Style)}", FontSize = 15, FontWeight = FontWeights.SemiBold });
        styleStack.Children.Add(new TextBlock { Text = ChordPattern.StyleDescription(line.Style), FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var style = Ui.Card(styleStack, 12);
        style.CornerRadius = new CornerRadius(10);
        _page.Children.Add(style);

        var selected = Math.Clamp(_pinned ?? (line.Playing >= 0 ? line.Playing : 0), 0, line.Bars.Count - 1);
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 2) };
        for (var i = 0; i < line.Bars.Count; i++)
        {
            tiles.Children.Add(Tile(line.Bars[i], i, key, i == selected, i == line.Playing));
        }

        _page.Children.Add(tiles);

        var bar = line.Bars[selected];
        var numeral = Theory.Numeral(bar.Chord, key.Root, key.Minor);
        var flats = NoteSpelling.Mode switch { 1 => false, 2 => true, _ => NoteSpelling.KeyUsesFlats(key.Root, key.Minor) };
        var played = bar.Notes.Select(n => ((int)n.Pitch, (double)(n.Start - bar.Start) / bar.Length, (double)(n.End - n.Start) / bar.Length)).ToList();
        var analysis = LineAnalysis.Analyse(bar.Chord, bar.Next, selected + 1, numeral, played, flats);
        _page.Children.Add(new RoleLaneControl { Height = 128, Notes = analysis.Notes, Margin = new Thickness(0, 8, 0, 8) });
        _page.Children.Add(Legend(analysis.Notes));
        _page.Children.Add(Explanation(analysis));
        _page.Children.Add(Actions(bar, line));
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private UIElement Tile(MainViewModel.BassBar bar, int index, SongKey key, bool selected, bool playing)
    {
        var numeral = Theory.Numeral(bar.Chord, key.Root, key.Minor);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = numeral, FontFamily = new FontFamily("Georgia"), FontSize = 22, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var name = NoteSpelling.NameFor(bar.Chord.Root, NoteSpelling.KeyUsesFlats(key.Root, key.Minor)) + bar.Chord.QualitySuffix;
        stack.Children.Add(new TextBlock { Text = $"{name} · bar {index + 1}{(playing ? " ▶ playing" : "")}", FontSize = 13, Foreground = Frozen(0xD6, 0xD6, 0xDA), HorizontalAlignment = HorizontalAlignment.Center });
        var tile = new Border
        {
            Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Click to see what each note in this bar is doing",
            Child = stack,
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            _pinned = index;
            _signature = "";
            Update();
        };
        return tile;
    }

    private static UIElement Legend(IReadOnlyList<LineNote> notes)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var roles = new List<(NoteRole Role, string Label)> { (NoteRole.Root, "Root"), (NoteRole.Fifth, "Fifth"), (NoteRole.Third, "Third"), (NoteRole.Octave, "Octave") };
        if (notes.Any(n => n.Role == NoteRole.Seventh))
        {
            roles.Add((NoteRole.Seventh, "Seventh"));
        }

        roles.Add((NoteRole.Approach, "Approach or passing note"));
        foreach (var (role, label) in roles)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            item.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(RoleLaneControl.RoleColor(role)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Frozen(0xCF, 0xCF, 0xD4) });
            panel.Children.Add(item);
        }

        return panel;
    }

    private UIElement Explanation(BarAnalysis analysis)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = analysis.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = analysis.Explanation, FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("Try it: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.Add(new System.Windows.Documents.Run(analysis.Tip));
        stack.Children.Add(new Border
        {
            Background = Frozen(0x1B, 0x2A, 0x22),
            BorderBrush = Ui.GoodBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = tip,
        });
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }

    private UIElement Actions(MainViewModel.BassBar bar, MainViewModel.BassLineInfo line)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var hear = Ui.Accent("Hear this bar", "Play this bar of the bass line");
        hear.Margin = new Thickness(0, 0, 8, 0);
        hear.OnClick(async () => await _context.Main.HearBar(bar));
        row.Children.Add(hear);
        var muted = _context.Main.BassIsMuted;
        var along = Ui.Secondary(muted ? "Bass is muted: unmute it" : "Play along: mute the bass for me", muted ? "Bring the bass machine back in" : "Mute the bass machine and play from this bar, so you can play the line yourself", 230);
        along.Click += (_, _) =>
        {
            if (_context.Main.BassIsMuted)
            {
                _context.Main.SetBassMuted(false);
            }
            else
            {
                _context.Main.PlayAlongFromBar(bar);
            }

            _signature = "";
            Update();
        };
        row.Children.Add(along);
        var neck = Ui.Secondary("Show on the bass neck", "Open the bass neck and the play-along tab", 170);
        neck.Click += (_, _) => LearnCatalog.OpenPractice(_context);
        row.Children.Add(neck);
        return row;
    }
}
