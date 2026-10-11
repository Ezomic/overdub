using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class ScaleExplorerLesson : UserControl
{
    private static readonly string[] RootNames = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private int _root;
    private ScaleDef _scale = ScaleExplorer.All[0];

    public ScaleExplorerLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        if (context.Main.DetectSongKeyQuick() is { } key)
        {
            UseSongKey(key);
        }

        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void UseSongKey(SongKey key)
    {
        _root = key.Root;
        _scale = ScaleExplorer.Find(key.Minor ? "Natural minor" : "Major");
    }

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Scale and mode explorer"));
        _page.Children.Add(Ui.Sub("Pick a root and a scale. See its notes on the bass neck and hear how it sounds.", 12));
        if (_context.Main.DetectSongKeyQuick() is { } key)
        {
            _page.Children.Add(SongBanner(key));
        }

        _page.Children.Add(Ui.Label("Root note"));
        _page.Children.Add(Chips(RootNames.Select((n, i) => (n, i == _root, (Action)(() => { _root = i; Render(); }))), 12));
        _page.Children.Add(Ui.Label("Scale or mode"));
        _page.Children.Add(Chips(ScaleExplorer.All.Select(s => (s.Name, s == _scale, (Action)(() => { _scale = s; Render(); }))), 14));

        var notes = ScaleExplorer.Notes(_scale, _root);
        var rootName = notes[0].Name;
        _page.Children.Add(new TextBlock { Text = $"{rootName} {_scale.Name}: {string.Join(" ", notes.Select(n => n.Name))}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        _page.Children.Add(Degrees(notes));
        _page.Children.Add(new ScaleNeckControl { Height = 150, Notes = notes, Margin = new Thickness(0, 0, 0, 0) });
        _page.Children.Add(Legend());
        _page.Children.Add(Explanation(notes));
        _page.Children.Add(Actions());
    }

    private UIElement SongBanner(SongKey key)
    {
        var dock = new DockPanel();
        var use = Ui.Secondary("Use my song's key", "Set the root and scale to your song's key", 140);
        use.Margin = new Thickness(12, 0, 0, 0);
        use.Click += (_, _) =>
        {
            UseSongKey(key);
            Render();
        };
        DockPanel.SetDock(use, Dock.Right);
        dock.Children.Add(use);
        var text = $"Your song is in {key.Name}.";
        if (ScaleExplorer.SameNotesAs(_scale, _root, key))
        {
            text += $" {ScaleExplorer.Notes(_scale, _root)[0].Name} {_scale.Name} uses the same notes.";
        }

        dock.Children.Add(new TextBlock { Text = text, FontSize = 13.5, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var card = Ui.Card(dock, 12);
        card.CornerRadius = new CornerRadius(10);
        return card;
    }

    private static UIElement Chips(IEnumerable<(string Text, bool Selected, Action Click)> items, double bottom)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, bottom) };
        foreach (var (text, selected, click) in items)
        {
            var chip = new Border
            {
                Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
                BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, FontSize = 13 },
            };
            chip.OnClick(click);
            panel.Children.Add(chip);
        }

        return panel;
    }

    private static UIElement Degrees(IReadOnlyList<ScaleNote> notes)
    {
        var grid = new UniformGrid { Columns = notes.Count, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var note in notes)
        {
            var colour = new SolidColorBrush(ScaleNeckControl.RoleColor(note.Role));
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
            stack.Children.Add(new TextBlock { Text = note.Label, FontFamily = new FontFamily("Georgia"), FontSize = 19, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = note.Name, FontSize = 13, Foreground = Frozen(0xD6, 0xD6, 0xDA), HorizontalAlignment = HorizontalAlignment.Center });
            var dock = new DockPanel();
            var bar = new Border { Height = 3, Background = colour };
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(stack);
            grid.Children.Add(new Border
            {
                Background = Ui.Res("Panel"),
                BorderBrush = Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 6, 0),
                ClipToBounds = true,
                Child = dock,
            });
        }

        return grid;
    }

    private UIElement Legend()
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) };
        var entries = new List<(ScaleRole Role, string Text)> { (ScaleRole.Root, "Root"), (ScaleRole.ChordTone, "Chord tones (1 3 5)"), (ScaleRole.Other, "Other scale notes") };
        if (_scale.Colour >= 0)
        {
            entries.Add((ScaleRole.Colour, $"Colour note (what makes it {_scale.Name})"));
        }

        foreach (var (role, text) in entries)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            item.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ScaleNeckControl.RoleColor(role)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Frozen(0xCF, 0xCF, 0xD4) });
            panel.Children.Add(item);
        }

        return panel;
    }

    private UIElement Explanation(IReadOnlyList<ScaleNote> notes)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"What {_scale.Name} sounds like", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = _scale.Sound, FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }.Glossed(_context));
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("Try it: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.AddRange(Gloss.Inlines(ScaleExplorer.Tip(_scale, _root), _context));
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

    private UIElement Actions()
    {
        var row = new WrapPanel();
        var hear = Ui.Accent("Hear the scale", "Play the scale up one octave");
        hear.Margin = new Thickness(0, 0, 8, 0);
        hear.OnClick(async () => await _context.Main.HearNotes(ScaleExplorer.Melody(_scale, _root)));
        row.Children.Add(hear);
        var drone = Ui.Secondary("Hear it over a drone", "Play the scale while the root note is held underneath", 160);
        drone.Click += async (_, _) => await _context.Main.HearNotes(ScaleExplorer.OverDrone(_scale, _root));
        row.Children.Add(drone);
        if (ScaleExplorer.CompareWith(_scale) is { } other)
        {
            var compare = Ui.Secondary($"Compare with {other.Name.ToLowerInvariant()}", $"Play this scale, then {other.Name} from the same root", 210);
            compare.Click += async (_, _) => await _context.Main.HearNotes(ScaleExplorer.Compare(_scale, other, _root));
            row.Children.Add(compare);
        }

        return row;
    }
}
