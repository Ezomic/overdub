using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class StructureLesson : UserControl
{
    private sealed record Part(string Name, int Bars, SectionKind Kind);

    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private int _style;
    private int _selected;

    public StructureLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        _selected = DefaultSelection(Parts(Template));
        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Quiet = Frozen(0x2F, 0x2F, 0x37);
    private static readonly Brush Blue = Frozen(0x6B, 0x8D, 0xE3);
    private static readonly Brush Amber = Frozen(0xE6, 0xA2, 0x3C);
    private static readonly Brush Purple = Frozen(0xB4, 0x8B, 0xE3);
    private static readonly Brush Teal = Frozen(0x4C, 0xC3, 0xA0);
    private static readonly Brush Coral = Frozen(0xE3, 0x6B, 0x8D);
    private static readonly Brush DarkInk = Frozen(0x14, 0x14, 0x18);

    private SongTemplate Template => SongTemplates.All.First(t => t.Name == SongStructure.Styles[_style].Template);

    private static List<Part> Parts(SongTemplate template)
    {
        var parts = new List<Part>();
        foreach (var plan in template.Sections)
        {
            var kind = SongStructure.KindOf(plan.Name);
            var prefix = plan.Name.Split(',')[0];
            if (kind == SectionKind.Round && parts.Count > 0 && parts[^1].Name == prefix)
            {
                parts[^1] = parts[^1] with { Bars = parts[^1].Bars + plan.Bars };
                continue;
            }

            parts.Add(new Part(kind == SectionKind.Round ? prefix : plan.Name, plan.Bars, kind));
        }

        return parts;
    }

    private static int DefaultSelection(List<Part> parts)
    {
        var index = parts.FindIndex(p => p.Kind is SectionKind.Chorus or SectionKind.Drop or SectionKind.Head or SectionKind.Round);
        return Math.Max(0, index);
    }

    private static Brush Fill(SectionKind kind) => kind switch
    {
        SectionKind.Intro or SectionKind.Outro or SectionKind.Other => Quiet,
        SectionKind.Verse or SectionKind.Round or SectionKind.Head => Blue,
        SectionKind.Chorus or SectionKind.Drop => Amber,
        SectionKind.Bridge or SectionKind.Turnaround => Purple,
        SectionKind.Solo or SectionKind.Build => Teal,
        _ => Coral,
    };

    private static string KindName(SectionKind kind) => kind switch
    {
        SectionKind.Round => "12-bar round",
        _ => kind.ToString(),
    };

    private void Render()
    {
        var template = Template;
        var parts = Parts(template);
        _selected = Math.Clamp(_selected, 0, parts.Count - 1);
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Song structure by style"));
        _page.Children.Add(Ui.Sub("Every style arranges its sections differently. Pick a style, see its map, and click a section to learn its job.", 12));
        _page.Children.Add(Ui.Label("Style"));
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < SongStructure.Styles.Count; i++)
        {
            var index = i;
            chips.Children.Add(Chip(SongStructure.Styles[i].Name, i == _style, () =>
            {
                _style = index;
                _selected = DefaultSelection(Parts(Template));
                Render();
            }));
        }

        _page.Children.Add(chips);
        _page.Children.Add(MapCard(template, parts));
        _page.Children.Add(DetailCard(parts[_selected]));
        _page.Children.Add(YourSong(parts));
        _page.Children.Add(Buttons(template, parts[_selected]));
    }

    private static Border Chip(string text, bool selected, Action click)
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
        return chip;
    }

    private static Border Card(UIElement child, double bottom = 12)
    {
        var card = Ui.Card(child, bottom);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(20, 14, 20, 14);
        return card;
    }

    private static string Title(StyleGuide style, List<Part> parts)
    {
        if (parts.All(p => p.Kind == SectionKind.Round))
        {
            return $"{style.Name}: {parts.Count} rounds of the same 12 bars";
        }

        var names = parts
            .Where(p => p.Kind is not (SectionKind.Intro or SectionKind.Outro))
            .Select(p => string.Concat(p.Name.TrimEnd('2', '3', ' ')).Replace("Last ", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant());
        return $"{style.Name}: {string.Join(", ", names)}";
    }

    private UIElement MapCard(SongTemplate template, List<Part> parts)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var totalBars = parts.Sum(p => p.Bars);
        var minutes = totalBars * 4 * 60.0 / template.Bpm / 60;
        var length = minutes < 1.5 ? "about a minute" : $"about {Math.Round(minutes):0} minutes";
        var side = new TextBlock { Text = $"{totalBars} bars at {template.Bpm:0} BPM, {length}", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(side, Dock.Right);
        head.Children.Add(side);
        head.Children.Add(new TextBlock { Text = Title(SongStructure.Styles[_style], parts), FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock { Text = SongStructure.Styles[_style].Overview, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        var map = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < parts.Count; i++)
        {
            map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].Bars, GridUnitType.Star), MinWidth = 44 });
            var index = i;
            var quiet = Fill(parts[i].Kind) == Quiet;
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ink = quiet ? Ui.Res("Text") : DarkInk;
            label.Children.Add(new TextBlock { Text = parts[i].Name, FontSize = 12.5, Foreground = ink, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = i == _selected ? FontWeights.SemiBold : FontWeights.Normal });
            label.Children.Add(new TextBlock { Text = parts[i].Bars.ToString(), FontSize = 12.5, Foreground = quiet ? Ui.Res("TextDim") : ink, TextAlignment = TextAlignment.Center });
            var block = new Border
            {
                Background = Fill(parts[i].Kind),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(3, 10, 3, 10),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = i == _selected ? Brushes.White : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = $"{parts[i].Name}: {parts[i].Bars} bars. Click to see what it does.",
                Child = label,
            };
            block.OnClick(() =>
            {
                _selected = index;
                Render();
            });
            Grid.SetColumn(block, i);
            map.Children.Add(block);
        }

        stack.Children.Add(map);
        var ends = new DockPanel();
        var end = new TextBlock { Text = "end", FontSize = 11.5, Foreground = Ui.Res("TextDim") };
        DockPanel.SetDock(end, Dock.Right);
        ends.Children.Add(end);
        ends.Children.Add(new TextBlock { Text = "start", FontSize = 11.5, Foreground = Ui.Res("TextDim") });
        stack.Children.Add(ends);
        return Card(stack);
    }

    private UIElement DetailCard(Part part)
    {
        var guide = SongStructure.GuideFor(part.Kind);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"Section · {KindName(part.Kind)} · {guide.Typical}", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(new TextBlock { Text = guide.Job, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = guide.Detail, FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var players = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (name, text) in new[] { ("Drums", guide.Drums), ("Guitar", guide.Guitar), ("Bass", guide.Bass), ("Keys", guide.Keys) })
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = Ui.Res("TextDim") });
            cell.Children.Add(new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            players.Children.Add(new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 0), Child = cell });
        }

        stack.Children.Add(players);
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("Bass tip: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.Add(new System.Windows.Documents.Run(guide.Tip));
        stack.Children.Add(new Border { Background = Frozen(0x1B, 0x2A, 0x22), BorderBrush = Ui.GoodBrush, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), Child = tip });
        return Card(stack);
    }

    private UIElement YourSong(List<Part> templateParts)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Your song", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var sections = _context.Main.Sections;
        if (sections.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "Your song has no marked sections yet. Use Build this in my song to lay this style out, or mark sections on the Record page (Sections menu).", FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap });
            return Card(stack);
        }

        var yours = sections.Select(s => new Part(s.Name, Math.Max(1, _context.Main.SectionBars(s)), SongStructure.KindOf(s.Name))).ToList();
        var map = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < yours.Count; i++)
        {
            map.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(yours[i].Bars, GridUnitType.Star), MinWidth = 44 });
            var quiet = Fill(yours[i].Kind) == Quiet;
            var block = new Border
            {
                Background = Fill(yours[i].Kind),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(3, 6, 3, 6),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                Child = new TextBlock { Text = $"{yours[i].Name} {yours[i].Bars}", FontSize = 12.5, Foreground = quiet ? Ui.Res("Text") : DarkInk, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            Grid.SetColumn(block, i);
            map.Children.Add(block);
        }

        stack.Children.Add(map);
        var have = yours.Select(p => p.Kind).ToHashSet();
        var wanted = templateParts.Select(p => p.Kind).Where(k => k is not (SectionKind.Verse or SectionKind.Chorus or SectionKind.Other or SectionKind.Round)).Distinct().ToList();
        var missing = wanted.Where(k => !have.Contains(k)).ToList();
        var total = yours.Sum(p => p.Bars);
        var text = missing.Count == 0
            ? $"Your song has {total} bars and every kind of section this style uses."
            : $"Your song has {total} bars and no {JoinOr(missing.Select(k => k.ToString().ToLowerInvariant()).ToList())}. {SongStructure.GuideFor(missing.FirstOrDefault(k => k is SectionKind.Bridge or SectionKind.Solo or SectionKind.Breakdown, missing[0])).Missing}";
        stack.Children.Add(new TextBlock { Text = text, FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap });
        return Card(stack);
    }

    private static string JoinOr(List<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} or {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} or {items[^1]}",
    };

    private UIElement Buttons(SongTemplate template, Part part)
    {
        var row = new WrapPanel();
        var build = Ui.Accent("Build this in my song", "Lay this style out as sections with drums, guitar, bass and a lead line, after what is already in your song");
        build.Margin = new Thickness(0, 0, 8, 6);
        build.OnClick(() =>
        {
            _context.Main.ApplyTemplate(template);
            Render();
        });
        row.Children.Add(build);
        var jump = Ui.Secondary("Jump to this section in my song", "Move the playhead to the first section in your song of this kind", 230);
        jump.Click += (_, _) =>
        {
            var match = _context.Main.Sections.FirstOrDefault(s => SongStructure.KindOf(s.Name) == part.Kind);
            if (match is null)
            {
                _context.Main.ShowNotice($"Your song has no {KindName(part.Kind).ToLowerInvariant()} yet.");
                return;
            }

            _context.Main.GoToSection(match);
            _context.Main.ShowNotice($"Playhead moved to {match.Name}.");
        };
        row.Children.Add(jump);
        return row;
    }
}
