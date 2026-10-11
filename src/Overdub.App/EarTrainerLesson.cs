using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class EarTrainerLesson : UserControl
{
    private const int QuestionCount = 10;
    private static readonly Brush Wrong = Frozen(0xE3, 0x6B, 0x6B);
    private static readonly Brush WrongTint = Frozen(0x33, 0x20, 0x1F);
    private static readonly Brush GoodTint = Frozen(0x1F, 0x33, 0x26);
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private EarKind _kind = EarKind.Intervals;
    private EarLevel _level = EarLevel.Easy;
    private bool _rootFirst = true;
    private IReadOnlyList<EarQuestion> _questions = [];
    private int _index;
    private int _correct;
    private int _streak;
    private int _best;
    private int? _picked;
    private bool _showNeck;
    private readonly List<EarKind> _missed = [];

    public EarTrainerLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        Start();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private bool UsesRootFirst => _kind is EarKind.MajorMinor or EarKind.ChordType or EarKind.BassNote;

    private void Start()
    {
        _questions = EarTraining.Build(_kind, _level, QuestionCount, new Random());
        _index = 0;
        _correct = 0;
        _streak = 0;
        _best = 0;
        _picked = null;
        _showNeck = false;
        _missed.Clear();
        Render(autoPlay: true);
    }

    private void Render(bool autoPlay)
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Ear trainer"));
        _page.Children.Add(Ui.Sub("Listen, then pick what you heard. Your bass machine plays the sounds.", 12));
        _page.Children.Add(Ui.Label("What to train"));
        _page.Children.Add(Chips(Enum.GetValues<EarKind>().Select(k => (EarTraining.KindName(k), k == _kind, (Action)(() => { _kind = k; Start(); }))), 10));
        _page.Children.Add(Settings());
        if (_index >= _questions.Count)
        {
            _page.Children.Add(Result());
            return;
        }

        var question = _questions[_index];
        _page.Children.Add(Progress());
        _page.Children.Add(QuestionCard(question));
        _page.Children.Add(Actions(question));
        if (_showNeck && _picked is not null)
        {
            _page.Children.Add(new ScaleNeckControl { Height = 150, Notes = question.Neck, Margin = new Thickness(0, 12, 0, 0) });
        }

        if (autoPlay)
        {
            Play(question, reveal: false);
        }
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

    private UIElement Settings()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var level = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
        level.Children.Add(new TextBlock { Text = "Level", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
        level.Children.Add(Chips(Enum.GetValues<EarLevel>().Select(l => (l.ToString(), l == _level, (Action)(() => { _level = l; Start(); }))), 0));
        row.Children.Add(level);
        if (UsesRootFirst)
        {
            var box = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = _rootFirst ? Ui.AccentBrush : Ui.Res("Border"), Background = _rootFirst ? Ui.AccentTint : Ui.Res("Panel"), Margin = new Thickness(0, 0, 8, 0), Child = new TextBlock { Text = _rootFirst ? "✓" : "", FontSize = 12, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 6), ToolTip = "Play a reference note before each question, so you have something to hear it against" };
            toggle.Children.Add(box);
            toggle.Children.Add(new TextBlock { Text = _kind == EarKind.BassNote ? "Play a C first" : "Play the root first", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            toggle.MouseLeftButtonUp += (_, _) =>
            {
                _rootFirst = !_rootFirst;
                Render(autoPlay: false);
            };
            row.Children.Add(toggle);
        }

        return row;
    }

    private UIElement Progress()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var score = new TextBlock { Text = $"{_correct} correct so far, best streak {_best}", FontSize = 12.5, Foreground = Ui.Res("TextDim") };
        DockPanel.SetDock(score, Dock.Right);
        line.Children.Add(score);
        line.Children.Add(new TextBlock { Text = $"Question {_index + 1} of {_questions.Count}", FontSize = 12.5, Foreground = Ui.Res("TextDim") });
        stack.Children.Add(line);
        var bar = new Grid { Height = 5 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_index, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_questions.Count - _index, GridUnitType.Star) });
        var track = new Border { Background = Ui.Res("Panel"), CornerRadius = new CornerRadius(3) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = Ui.AccentBrush, CornerRadius = new CornerRadius(3) });
        stack.Children.Add(bar);
        return stack;
    }

    private string Hint(EarQuestion question) => question.Kind switch
    {
        EarKind.Intervals => "Played: the low note, then the high note",
        EarKind.MajorMinor when _level == EarLevel.Medium => "Played: the notes one after the other",
        EarKind.MajorMinor or EarKind.ChordType => "Played: all the notes at once",
        EarKind.Scale => "Played: up the scale to the octave",
        _ => _rootFirst ? "Played: a C, then the note" : "Played: the note on its own",
    };

    private UIElement QuestionCard(EarQuestion question)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"{EarTraining.KindName(question.Kind)} · {_level}", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = question.Prompt, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        var play = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var circle = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(23),
            Background = Ui.AccentBrush,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Play the question",
            Child = new TextBlock { Text = "▶", FontSize = 18, Foreground = Ui.AccentText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) },
        };
        circle.OnClick(() => Play(question, reveal: false));
        play.Children.Add(circle);
        var again = Ui.Secondary("Play again", "Hear the question again", 90);
        again.Click += (_, _) => Play(question, reveal: false);
        play.Children.Add(again);
        if (question.Kind == EarKind.Intervals)
        {
            var together = Ui.Secondary("Together", "Play both notes at the same time", 90);
            together.Click += (_, _) => PlayNotes(question.Reveal.Skip(2).ToList().Select(n => (n.Pitch, 0, n.LenMs)).ToList());
            play.Children.Add(together);
        }

        play.Children.Add(new TextBlock { Text = Hint(question), FontSize = 12.5, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 6) });
        stack.Children.Add(play);

        var grid = new UniformGrid { Columns = 2 };
        for (var i = 0; i < question.Options.Count; i++)
        {
            grid.Children.Add(Option(question, i));
        }

        stack.Children.Add(grid);
        if (_picked is not null)
        {
            var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
            tip.Inlines.Add(new System.Windows.Documents.Run("How to remember it: ") { FontWeight = FontWeights.SemiBold });
            tip.Inlines.AddRange(Gloss.Inlines(question.Tip, _context));
            stack.Children.Add(new Border
            {
                Background = Frozen(0x1B, 0x2A, 0x22),
                BorderBrush = Ui.GoodBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 12, 0, 0),
                Child = tip,
            });
        }

        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(20, 16, 20, 18);
        return card;
    }

    private UIElement Option(EarQuestion question, int index)
    {
        var answered = _picked is not null;
        var isCorrect = index == question.Correct;
        var isPick = _picked == index;
        var text = question.Options[index];
        if (answered && isCorrect)
        {
            text += "   ✓ correct";
        }
        else if (answered && isPick)
        {
            text += "   your pick";
        }

        var border = new Border
        {
            Background = answered && isCorrect ? GoodTint : answered && isPick ? WrongTint : Ui.Res("Panel"),
            BorderBrush = answered && isCorrect ? Ui.GoodBrush : answered && isPick ? Wrong : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = answered ? Cursors.Arrow : Cursors.Hand,
            Child = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap },
        };
        if (!answered)
        {
            border.MouseEnter += (_, _) => border.Background = Ui.Hover;
            border.MouseLeave += (_, _) => border.Background = Ui.Res("Panel");
            border.OnClick(() => Pick(question, index));
        }

        return border;
    }

    private void Pick(EarQuestion question, int index)
    {
        _picked = index;
        if (index == question.Correct)
        {
            _correct++;
            _streak++;
            _best = Math.Max(_best, _streak);
        }
        else
        {
            _streak = 0;
            _missed.Add(question.Kind);
        }

        Render(autoPlay: false);
    }

    private UIElement Actions(EarQuestion question)
    {
        var row = new WrapPanel();
        if (_picked is null)
        {
            return row;
        }

        var last = _index == _questions.Count - 1;
        var next = Ui.Accent(last ? "See your score" : "Next question");
        next.Margin = new Thickness(0, 0, 8, 6);
        next.OnClick(() =>
        {
            _index++;
            _picked = null;
            _showNeck = false;
            Render(autoPlay: true);
        });
        row.Children.Add(next);
        var hear = Ui.Secondary("Hear the answer", "Hear the notes again, one by one and then together", 130);
        hear.Click += (_, _) => Play(question, reveal: true);
        row.Children.Add(hear);
        var neck = Ui.Secondary(_showNeck ? "Hide the bass neck" : "Show on the bass neck", "Show where these notes are on the bass neck", 160);
        neck.Click += (_, _) =>
        {
            _showNeck = !_showNeck;
            Render(autoPlay: false);
        };
        row.Children.Add(neck);
        return row;
    }

    private void Play(EarQuestion question, bool reveal)
    {
        if (reveal)
        {
            PlayNotes(question.Reveal);
            return;
        }

        var notes = question.Play;
        if (_rootFirst && UsesRootFirst && question.Kind != EarKind.BassNote)
        {
            notes = new[] { (question.RootPitch, 0, 700) }.Concat(notes.Select(n => (n.Pitch, n.AtMs + 1000, n.LenMs))).ToList();
        }
        else if (_rootFirst && question.Kind == EarKind.BassNote)
        {
            notes = new[] { (EarTraining.ReferencePitch, 0, 700) }.Concat(notes.Select(n => (n.Pitch, n.AtMs + 1000, n.LenMs))).ToList();
        }

        PlayNotes(notes);
    }

    private void PlayNotes(IReadOnlyList<(int Pitch, int AtMs, int LenMs)> notes)
    {
        if (!_context.Main.Tracks.Any(t => t.Model.Machine is not null))
        {
            _context.Main.ShowMessage("Add a bass machine first (Build a song, then Add a player) so there is something to play the sounds.");
            return;
        }

        _ = _context.Main.HearNotes(notes);
    }

    private UIElement Result()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"You got {_correct} of {_questions.Count}", FontSize = 20, FontWeight = FontWeights.SemiBold });
        var message = _correct == _questions.Count ? "Perfect round." : _correct >= _questions.Count * 0.7 ? "Good ears. Try the next level when you are ready." : "Ears take practice. Another round gives new questions, and the tips help you remember each sound.";
        stack.Children.Add(new TextBlock { Text = $"Best streak: {_best}. {message}", FontSize = 13.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        var again = Ui.Accent("Another round");
        again.OnClick(Start);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(again);
        stack.Children.Add(row);
        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 16);
        return card;
    }
}
