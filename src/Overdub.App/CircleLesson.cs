using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class CircleLesson : UserControl
{
    private const int MaxChords = 4;
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly List<int> _degrees = [];
    private int _index;
    private bool _minor;

    public CircleLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        if (context.Main.DetectSongKeyQuick() is { } key)
        {
            _minor = key.Minor;
            _index = Math.Max(0, CircleOfFifths.IndexOf(key.Root, key.Minor));
        }

        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private int Root => CircleOfFifths.KeyRoot(_index, _minor);

    private bool Flats => NoteSpelling.Mode switch { 1 => false, 2 => true, _ => CircleOfFifths.UsesFlats(_index) };

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Circle of fifths and progression builder"));
        _page.Children.Add(Ui.Sub("The circle shows which keys and chords sit close together. Pick chords from your key to build a progression and hear it.", 12));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(348) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        var circle = new CircleControl { Selected = _index, Minor = _minor, HorizontalAlignment = HorizontalAlignment.Left };
        circle.KeySelected += (index, minor) =>
        {
            if (minor != _minor)
            {
                _degrees.Clear();
            }

            _index = index;
            _minor = minor;
            Render();
        };
        left.Children.Add(circle);
        left.Children.Add(new TextBlock { Text = "Outer ring: major keys. Inner ring: their relative minors. Clockwise goes up a fifth, each step adds one sharp. Dashed rings show the neighbours.", FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 0) });
        grid.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 1);
        right.Children.Add(new TextBlock { Text = $"Key of {CircleOfFifths.KeyName(_index, _minor)}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        right.Children.Add(new TextBlock { Text = $"{CircleOfFifths.Signature(_index)}. {CircleOfFifths.Neighbours(_index, _minor)}", FontSize = 13, LineHeight = 19, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }.Glossed(_context));
        right.Children.Add(Ui.Label("Chords in this key (tap to add)"));
        right.Children.Add(KeyChords());
        right.Children.Add(Ui.Label("Your progression"));
        right.Children.Add(Progression());
        right.Children.Add(Explanation());
        right.Children.Add(Actions());
        grid.Children.Add(right);
        _page.Children.Add(grid);
    }

    private UIElement Tile(int degree, bool picked)
    {
        var chord = CircleOfFifths.ChordFor(Root, _minor, degree);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = Theory.Numeral(chord, Root, _minor), FontFamily = new FontFamily("Georgia"), FontSize = 18, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = CircleOfFifths.ChordLabel(chord, Flats), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center });
        return new Border
        {
            Background = picked ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = picked ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 6),
            Child = stack,
        };
    }

    private UIElement KeyChords()
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (var degree = 1; degree <= 7; degree++)
        {
            var chosen = degree;
            var tile = (Border)Tile(chosen, false);
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = Theory.Describe(CircleOfFifths.ChordFor(Root, _minor, chosen), Root, _minor).Title;
            tile.OnClick(() =>
            {
                if (_degrees.Count >= MaxChords)
                {
                    _context.Main.ShowNotice($"A progression holds up to {MaxChords} chords. Remove one first.");
                    return;
                }

                _degrees.Add(chosen);
                Render();
                HearCurrent();
            });
            panel.Children.Add(tile);
        }

        return panel;
    }

    private UIElement Progression()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var degree in _degrees)
        {
            row.Children.Add(Tile(degree, true));
        }

        if (_degrees.Count == 0)
        {
            row.Children.Add(new TextBlock { Text = "Nothing yet. Tap a chord above, or try a famous one.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var remove = new TextBlock { Text = "Remove last", FontSize = 12.5, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) };
            remove.MouseLeftButtonUp += (_, _) =>
            {
                _degrees.RemoveAt(_degrees.Count - 1);
                Render();
            };
            row.Children.Add(remove);
        }

        return row;
    }

    private UIElement Explanation()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        text.Text = CircleOfFifths.Explain(Root, _minor, _degrees);
        text.Glossed(_context);
        return new Border
        {
            Background = Frozen(0x1B, 0x2A, 0x22),
            BorderBrush = Ui.GoodBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = text,
        };
    }

    private UIElement Actions()
    {
        var row = new WrapPanel();
        var hear = Ui.Accent("Hear it", "Play the progression");
        hear.Margin = new Thickness(0, 0, 8, 6);
        hear.OnClick(async () =>
        {
            if (_degrees.Count == 0)
            {
                _context.Main.ShowNotice("Add a chord first.");
                return;
            }

            await HearCurrentAsync();
        });
        row.Children.Add(hear);
        var use = Ui.Secondary("Use in my song", "Put these chords on your guitar machine's first chord pattern (you can undo it)", 130);
        use.Click += (_, _) =>
        {
            if (_degrees.Count == 0)
            {
                _context.Main.ShowNotice("Add a chord first.");
                return;
            }

            _context.Main.UseProgression(Chords());
        };
        row.Children.Add(use);
        var famous = Ui.Secondary("Try a famous one ▾", "Fill the progression with a well-known one", 150);
        famous.Click += (_, _) => ShowFamous(famous);
        row.Children.Add(famous);
        return row;
    }

    private IReadOnlyList<Chord> Chords() => _degrees.Select(d => CircleOfFifths.ChordFor(Root, _minor, d)).ToList();

    private void HearCurrent() => _ = HearCurrentAsync();

    private Task HearCurrentAsync() => _context.Main.HearChords(Chords());

    private void ShowFamous(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var famous in CircleOfFifths.Famous.Where(f => f.Minor == _minor))
        {
            var chosen = famous;
            var numerals = string.Join(" ", chosen.Degrees.Select(d => Theory.Numeral(CircleOfFifths.ChordFor(Root, _minor, d), Root, _minor)));
            var item = new MenuItem { Header = $"{chosen.Name}   ({numerals})", ToolTip = chosen.Text };
            item.Click += (_, _) =>
            {
                _degrees.Clear();
                _degrees.AddRange(chosen.Degrees);
                Render();
                HearCurrent();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
