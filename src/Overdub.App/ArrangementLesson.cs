using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class EnergyControl : FrameworkElement
{
    public IReadOnlyList<int> Values { get; set; } = [];

    public int Max { get; set; } = 5;

    public Brush Line { get; set; } = Brushes.White;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 56);

    protected override void OnRender(DrawingContext context)
    {
        var well = TryFindResource("Well") as Brush ?? Brushes.Black;
        var border = TryFindResource("Border") as Brush ?? Brushes.DimGray;
        context.DrawRoundedRectangle(well, new Pen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        if (Values.Count == 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        var points = new List<Point>();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < Values.Count; i++)
            {
                var x = 20 + ((ActualWidth - 40) * (i + 0.5) / Values.Count);
                var y = ActualHeight - 8 - ((ActualHeight - 16) * Values[i] / Math.Max(1, Max));
                var point = new Point(x, y);
                points.Add(point);
                if (i == 0)
                {
                    g.BeginFigure(point, false, false);
                }
                else
                {
                    g.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(Line, 2.5) { LineJoin = PenLineJoin.Round }, geometry);
        foreach (var point in points)
        {
            context.DrawEllipse(Line, null, point, 4, 4);
        }
    }
}

public sealed class ArrangementLesson : UserControl
{
    private static readonly Brush Wrong = Frozen(0xE3, 0x6B, 0x6B);
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly Dictionary<ArrangeIdea, int> _appliedAt = [];
    private ArrangeIdea _idea;

    public ArrangementLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void Render()
    {
        var grid = _context.Main.BuildArrangeGrid();
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Arrangement: build a song that grows"));
        _page.Children.Add(Ui.Sub("A good arrangement decides who plays when. Try five ideas on your own song, hear them, and keep or undo each one.", 12));
        if (grid.Sections.Count == 0 || grid.Players.Count == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(new TextBlock { Text = "Your song needs marked sections and players first.", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            empty.Children.Add(new TextBlock { Text = "Open Song structure by style and use Build this in my song, or mark sections on the Record page (Sections menu). Then come back here.", FontSize = 13, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap });
            var card = Ui.Card(empty, 12);
            card.Padding = new Thickness(18, 14, 18, 14);
            _page.Children.Add(card);
            return;
        }

        var plan = Arrangement.Plan(grid, _idea == ArrangeIdea.MarkChanges ? ArrangeIdea.StartSmall : _idea);
        _page.Children.Add(GridCard(grid, _idea == ArrangeIdea.MarkChanges ? [] : plan));
        _page.Children.Add(IdeaCard(grid, plan));
        _page.Children.Add(new TextBlock { Text = "Muting a square only silences that player in that section. The blocks stay on the timeline, so you can bring them back at any time.", FontSize = 12.5, LineHeight = 18, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap });
    }

    private UIElement GridCard(ArrangeGrid grid, IReadOnlyList<(int Player, int Section, bool Active)> plan)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var hint = new TextBlock { Text = "click a square to mute or unmute that player in that section", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(new TextBlock { Text = "Who plays when", FontSize = 15, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(head);

        var table = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        for (var s = 0; s < grid.Sections.Count; s++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 44 });
        }

        for (var r = 0; r <= grid.Players.Count; r++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var s = 0; s < grid.Sections.Count; s++)
        {
            var label = new TextBlock { Text = grid.Sections[s].Name, FontSize = 12, Foreground = Ui.Res("TextDim"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 4) };
            Grid.SetColumn(label, s + 1);
            table.Children.Add(label);
        }

        for (var p = 0; p < grid.Players.Count; p++)
        {
            var name = new TextBlock { Text = grid.Players[p].Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, p + 1);
            table.Children.Add(name);
            var color = new SolidColorBrush(MixBarsControl.RoleColor(grid.Players[p].Role));
            for (var s = 0; s < grid.Sections.Count; s++)
            {
                var player = p;
                var section = s;
                var cell = grid[p, s];
                var proposed = plan.FirstOrDefault(c => c.Player == p && c.Section == s);
                var changing = plan.Any(c => c.Player == p && c.Section == s);
                var box = new Border
                {
                    Height = 24,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2),
                    BorderThickness = new Thickness(changing ? 2 : 1),
                    BorderBrush = changing ? Ui.AccentBrush : cell.Empty ? Ui.Res("Border") : color,
                    Background = cell.Empty ? Brushes.Transparent : cell.Active ? color : Ui.Res("Well"),
                    Cursor = cell.Empty ? Cursors.Arrow : Cursors.Hand,
                    ToolTip = cell.Empty ? $"{grid.Players[p].Name} has no block in {grid.Sections[s].Name}" : changing ? $"Would {(proposed.Active ? "unmute" : "mute")} {grid.Players[p].Name} in {grid.Sections[s].Name}" : $"{(cell.Active ? "Mute" : "Unmute")} {grid.Players[p].Name} in {grid.Sections[s].Name}",
                };
                if (!cell.Empty)
                {
                    box.OnClick(() =>
                    {
                        _context.Main.SetArrangement($"{(cell.Active ? "Mute" : "Unmute")} {grid.Players[player].Name} in {grid.Sections[section].Name}", grid, [(player, section, !cell.Active)]);
                        Render();
                    });
                }

                Grid.SetRow(box, p + 1);
                Grid.SetColumn(box, s + 1);
                table.Children.Add(box);
            }
        }

        stack.Children.Add(table);
        stack.Children.Add(new TextBlock { Text = "Energy: how many players are playing", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 4, 0, 4) });
        var energy = Enumerable.Range(0, grid.Sections.Count).Select(grid.Energy).ToList();
        var verdict = Arrangement.Verdict(grid);
        stack.Children.Add(new EnergyControl { Values = energy, Max = grid.Players.Count, Line = verdict.StartsWith("Flat") || verdict.StartsWith("The choruses") ? Wrong : Ui.GoodBrush });
        stack.Children.Add(new TextBlock { Text = verdict, FontSize = 12.5, Foreground = verdict.StartsWith("Flat") || verdict.StartsWith("The choruses") ? Wrong : Frozen(0xCF, 0xE6, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }

    private UIElement IdeaCard(ArrangeGrid grid, IReadOnlyList<(int Player, int Section, bool Active)> plan)
    {
        var stack = new StackPanel();
        var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var idea in Enum.GetValues<ArrangeIdea>())
        {
            var chosen = idea;
            var current = idea == _idea;
            var done = _appliedAt.ContainsKey(idea);
            var pill = new Border
            {
                Background = current ? Ui.AccentTint : done ? Frozen(0x1F, 0x33, 0x26) : Ui.Res("Panel"),
                BorderBrush = current ? Ui.AccentBrush : done ? Ui.GoodBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = $"{(done && !current ? "✓ " : "")}{(int)idea + 1} {Arrangement.IdeaNames[(int)idea]}", FontSize = 12.5, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, Foreground = current ? Frozen(0xF0, 0xDC, 0xB8) : done ? Frozen(0xCF, 0xE6, 0xD4) : Ui.Res("TextDim") },
            };
            pill.OnClick(() =>
            {
                _idea = chosen;
                Render();
            });
            pills.Children.Add(pill);
        }

        stack.Children.Add(pills);
        stack.Children.Add(new TextBlock { Text = Arrangement.IdeaNames[(int)_idea], FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = Arrangement.Explanations[(int)_idea], FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }.Glossed(_context));
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("What I would change: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.AddRange(Gloss.Inlines(Arrangement.Changes[(int)_idea], _context));
        stack.Children.Add(new Border { Background = Frozen(0x1B, 0x2A, 0x22), BorderBrush = Ui.GoodBrush, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), Child = tip });

        var row = new WrapPanel();
        var applied = _appliedAt.ContainsKey(_idea);
        var apply = Ui.Accent(applied ? "Applied" : Arrangement.ApplyLabels[(int)_idea], "Change your song. Undo puts it back.");
        apply.Margin = new Thickness(0, 0, 8, 6);
        apply.OnClick(() =>
        {
            if (applied)
            {
                return;
            }

            if (_idea == ArrangeIdea.MarkChanges)
            {
                var before = _context.Main.EditDepth;
                _context.Main.AddFillsAtSections();
                if (_context.Main.EditDepth > before)
                {
                    _appliedAt[_idea] = _context.Main.EditDepth;
                }
            }
            else if (plan.Count == 0)
            {
                _context.Main.ShowNotice("Your song is already arranged this way.");
                return;
            }
            else
            {
                _context.Main.SetArrangement($"Arrangement: {Arrangement.IdeaNames[(int)_idea]}", grid, plan);
                _appliedAt[_idea] = _context.Main.EditDepth;
            }

            Render();
        });
        row.Children.Add(apply);
        var hear = Ui.Secondary("Hear it from the top", "Play the song from its first section", 150);
        hear.Click += (_, _) => _context.Main.PlayFromSong(grid.Sections[0].Start);
        row.Children.Add(hear);
        var undo = Ui.Secondary("Undo", "Undo this idea", 70);
        undo.Click += (_, _) =>
        {
            if (!_appliedAt.TryGetValue(_idea, out var depth))
            {
                return;
            }

            if (_context.Main.EditDepth != depth)
            {
                _context.Main.ShowNotice("Other edits came after this one, so use Undo in the toolbar to step back through them.");
                return;
            }

            _context.Main.UndoLast();
            _appliedAt.Remove(_idea);
            Render();
        };
        row.Children.Add(undo);
        stack.Children.Add(row);
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }
}
