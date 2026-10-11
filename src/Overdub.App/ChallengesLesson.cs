using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class ChallengesLesson : UserControl
{
    private static readonly Brush WrongBrush = Frozen(0xE3, 0x6B, 0x6B);
    private static readonly Brush WrongTint = Frozen(0x33, 0x20, 0x1F);
    private static readonly Brush GoodTint = Frozen(0x1F, 0x33, 0x26);
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private TextBlock _status = NewStatus("");
    private ChallengeDef _def = PracticeChallenges.All[0];
    private ChallengeResult? _result;
    private string? _problem;
    private CancellationTokenSource? _running;
    private MainViewModel.ChallengePlan _plan = null!;

    public ChallengesLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _running?.Cancel();
            }
        };
        Render();
    }

    private static TextBlock NewStatus(string text) => new() { Text = text, FontSize = 12.5, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string Stars(int stars) => new string('★', stars) + new string('☆', 3 - stars);

    private void Choose(ChallengeDef def)
    {
        if (_running is not null)
        {
            return;
        }

        _def = def;
        _result = null;
        _problem = null;
        Render();
    }

    private void Render()
    {
        _plan = _context.Main.PrepareChallenge(_def);
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) =>
        {
            _running?.Cancel();
            _context.Back();
        };
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Practice challenges"));
        _page.Children.Add(Ui.Sub("Play your real bass over your song. Overdub listens and tells you what you hit.", 12));
        _page.Children.Add(Ui.Label("Pick a challenge"));
        _page.Children.Add(List());
        _page.Children.Add(ChallengeCard());
        if (_result is not null)
        {
            _page.Children.Add(ResultCard(_result));
        }
    }

    private UIElement List()
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var def in PracticeChallenges.All)
        {
            var chosen = def;
            var selected = def == _def;
            var best = ChallengeProgress.Best(def.Id);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = def.Level, FontSize = 12, Foreground = Ui.Res("TextDim") });
            stack.Children.Add(new TextBlock { Text = def.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) });
            stack.Children.Add(new TextBlock { Text = best < 0 ? "not tried" : best == 0 ? "tried, no stars yet" : $"{Stars(best)} best", FontSize = 12.5, Foreground = best <= 0 ? Ui.Res("TextDim") : Ui.AccentBrush });
            var card = new Border
            {
                Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
                BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Child = stack,
            };
            card.OnClick(() => Choose(chosen));
            grid.Children.Add(card);
        }

        return grid;
    }

    private UIElement ChallengeCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"{_def.Level} · Over your song, {PracticeChallenges.Bars} bars", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(new TextBlock { Text = _def.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = PracticeChallenges.Description(_def, _plan.Targets.Count), FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }.Glossed(_context));

        if (_plan.Problem is { } problem)
        {
            stack.Children.Add(new TextBlock { Text = problem, FontSize = 13, Foreground = WrongBrush, TextWrapping = TextWrapping.Wrap });
            var plain = Ui.Card(stack, 12);
            plain.CornerRadius = new CornerRadius(10);
            plain.Padding = new Thickness(20, 16, 20, 16);
            return plain;
        }

        if (PracticeChallenges.NeedsTargets(_def))
        {
            var tiles = new UniformGrid { Columns = PracticeChallenges.Bars, Margin = new Thickness(0, 0, 0, 12) };
            var flats = NoteSpelling.KeyUsesFlats(_plan.Key!.Root, _plan.Key.Minor);
            for (var bar = 0; bar < PracticeChallenges.Bars; bar++)
            {
                var tile = new StackPanel();
                tile.Children.Add(new TextBlock { Text = $"Bar {bar + 1} · {NoteSpelling.NameFor(_plan.Chords[bar].Root, flats)}{_plan.Chords[bar].QualitySuffix}", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
                tile.Children.Add(new TextBlock { Text = PracticeChallenges.BarSummary(_plan.Targets, bar), FontSize = 14, TextWrapping = TextWrapping.Wrap });
                tiles.Children.Add(new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 10, 0), Child = tile });
            }

            stack.Children.Add(tiles);
        }

        var row = new WrapPanel();
        var running = _running is not null;
        var start = Ui.Accent(running ? "Stop" : "Start with a count-in", running ? "Stop this take" : "Mute the bass machine, count in one bar, and record your bass for four bars");
        start.Margin = new Thickness(0, 0, 8, 6);
        start.OnClick(() =>
        {
            if (_running is not null)
            {
                _running.Cancel();
            }
            else
            {
                _ = RunAsync();
            }
        });
        row.Children.Add(start);
        if (PracticeChallenges.NeedsTargets(_def))
        {
            var hear = Ui.Secondary("Hear the target", "Play the notes you are asked to play", 120);
            hear.Click += (_, _) => _ = _context.Main.HearTargets(_plan);
            hear.IsEnabled = !running;
            row.Children.Add(hear);
        }

        _status = NewStatus(_problem ?? (running ? _status.Text : $"Bass input {(_plan.Input?.Input ?? 1) + 1} · song tempo {_context.Main.Bpm:0} BPM"));
        _status.Foreground = _problem is null ? Ui.Res("TextDim") : WrongBrush;
        _status.Margin = new Thickness(6, 0, 0, 6);
        row.Children.Add(_status);
        stack.Children.Add(row);

        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(20, 16, 20, 14);
        return card;
    }

    private async Task RunAsync()
    {
        _running = new CancellationTokenSource();
        _problem = null;
        _status.Text = "Getting ready...";
        var progress = new Progress<string>(text => _status.Text = text);
        Render();
        try
        {
            var (result, problem) = await _context.Main.RunChallengeAsync(_def, _plan, progress, _running.Token);
            _problem = problem;
            _result = result;
            if (result is not null)
            {
                ChallengeProgress.Record(_def.Id, result.Stars);
            }
        }
        finally
        {
            _running = null;
        }

        Render();
    }

    private UIElement ResultCard(ChallengeResult result)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var timing = new TextBlock { FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center };
        if (_def.Kind != ChallengeKind.Free && result.Notes.Any(n => n.Hit) && Math.Abs(result.AverageOffsetMs) >= 15)
        {
            timing.Text = $"timing was {Math.Abs(result.AverageOffsetMs):0} ms {(result.AverageOffsetMs < 0 ? "early" : "late")} on average";
        }
        else if (_def.Kind != ChallengeKind.Free && result.Notes.Any(n => n.Hit))
        {
            timing.Text = "timing was right on the beat";
        }

        DockPanel.SetDock(timing, Dock.Right);
        head.Children.Add(timing);
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
        title.Inlines.Add(new System.Windows.Documents.Run(_def.Kind == ChallengeKind.Free ? $"Last try: {result.Hits} of {result.Total} notes in key " : $"Last try: {result.Hits} of {result.Total} notes "));
        title.Inlines.Add(new System.Windows.Documents.Run(Stars(result.Stars)) { Foreground = Ui.AccentBrush });
        head.Children.Add(title);
        stack.Children.Add(head);

        var strip = new UniformGrid { Columns = Math.Clamp(result.Notes.Count, 1, 8), Margin = new Thickness(0, 0, 0, 10) };
        foreach (var note in result.Notes)
        {
            var late = note.Hit && note.State is "early" or "late";
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(new TextBlock { Text = note.Name, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = note.State, FontSize = 12, Foreground = note.Hit ? Ui.Res("TextDim") : WrongBrush, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            strip.Children.Add(new Border
            {
                Background = note.Hit ? GoodTint : WrongTint,
                BorderBrush = note.Hit ? Ui.GoodBrush : WrongBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 6, 2, 6),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = late ? $"{note.Name}: {note.State}" : null,
                Child = cell,
            });
        }

        stack.Children.Add(strip);
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("Tip: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.AddRange(Gloss.Inlines(result.Tip, _context));
        stack.Children.Add(new Border
        {
            Background = Frozen(0x1B, 0x2A, 0x22),
            BorderBrush = Ui.GoodBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = tip,
        });
        var row = new WrapPanel();
        var again = Ui.Secondary("Try again", "Play this challenge again", 90);
        again.Click += (_, _) => _ = RunAsync();
        row.Children.Add(again);
        var index = PracticeChallenges.All.ToList().IndexOf(_def);
        if (index < PracticeChallenges.All.Count - 1)
        {
            var next = Ui.Secondary("Next challenge", "Go to the next challenge", 130);
            next.Click += (_, _) => Choose(PracticeChallenges.All[index + 1]);
            row.Children.Add(next);
        }

        stack.Children.Add(row);
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(20, 16, 20, 14);
        return card;
    }
}
