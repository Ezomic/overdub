using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class TheoryQuizLesson : UserControl
{
    private const int QuestionCount = 10;
    private static readonly Brush Wrong = Frozen(0xE3, 0x6B, 0x6B);
    private static readonly Brush WrongTint = Frozen(0x33, 0x20, 0x1F);
    private static readonly Brush GoodTint = Frozen(0x1F, 0x33, 0x26);
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private QuizTopic _topic = QuizTopic.Mixed;
    private IReadOnlyList<QuizQuestion> _questions = [];
    private int _index;
    private int _correct;
    private int? _picked;
    private readonly List<QuizTopic> _missed = [];

    public TheoryQuizLesson(LearnContext context)
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

    private void Start()
    {
        var key = _context.Main.DetectSongKeyQuick();
        _questions = TheoryQuiz.Build(_topic, QuestionCount, new Random(), key, _context.Main.SongProgression());
        _index = 0;
        _correct = 0;
        _picked = null;
        _missed.Clear();
        Render();
    }

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Theory quiz"));
        _page.Children.Add(Ui.Sub("Short questions that check what you have learned. Some use your own song.", 12));
        _page.Children.Add(Ui.Label("Topic"));
        _page.Children.Add(Topics());
        if (_index >= _questions.Count)
        {
            _page.Children.Add(Result());
            return;
        }

        _page.Children.Add(Progress());
        _page.Children.Add(QuestionCard(_questions[_index]));
        _page.Children.Add(Actions(_questions[_index]));
    }

    private UIElement Topics()
    {
        var hasSong = _context.Main.DetectSongKeyQuick() is not null;
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var topic in Enum.GetValues<QuizTopic>().Where(t => t != QuizTopic.Song || hasSong))
        {
            var selected = topic == _topic;
            var chip = new Border
            {
                Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
                BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = TheoryQuiz.TopicName(topic), FontSize = 13 },
            };
            chip.OnClick(() =>
            {
                _topic = topic;
                Start();
            });
            panel.Children.Add(chip);
        }

        return panel;
    }

    private UIElement Progress()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var score = new TextBlock { Text = $"{_correct} correct so far", FontSize = 12.5, Foreground = Ui.Res("TextDim") };
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

    private UIElement QuestionCard(QuizQuestion question)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = TheoryQuiz.TopicName(question.Topic), FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = question.Text, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var grid = new UniformGrid { Columns = 2 };
        for (var i = 0; i < question.Options.Count; i++)
        {
            grid.Children.Add(Option(question, i));
        }

        stack.Children.Add(grid);
        if (_picked is not null)
        {
            var why = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
            why.Inlines.Add(new System.Windows.Documents.Run("Why: ") { FontWeight = FontWeights.SemiBold });
            why.Inlines.Add(new System.Windows.Documents.Run(question.Explanation));
            stack.Children.Add(new Border
            {
                Background = Frozen(0x1B, 0x2A, 0x22),
                BorderBrush = Ui.GoodBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 12, 0, 0),
                Child = why,
            });
        }

        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 16);
        return card;
    }

    private UIElement Option(QuizQuestion question, int index)
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

    private void Pick(QuizQuestion question, int index)
    {
        _picked = index;
        if (index == question.Correct)
        {
            _correct++;
        }
        else
        {
            _missed.Add(question.Topic);
        }

        Render();
    }

    private UIElement Actions(QuizQuestion question)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (_picked is not null)
        {
            var last = _index == _questions.Count - 1;
            var next = Ui.Accent(last ? "See your score" : "Next question");
            next.Margin = new Thickness(0, 0, 8, 0);
            next.OnClick(() =>
            {
                _index++;
                _picked = null;
                Render();
            });
            row.Children.Add(next);
        }

        if (question.Hear is { } chords)
        {
            var hear = Ui.Secondary("Hear it", "Play the chord from this question", 80);
            hear.Click += async (_, _) => await _context.Main.HearChords(chords);
            row.Children.Add(hear);
        }

        if (_picked is not null && LessonFor(question.Topic) is { } lesson)
        {
            var open = Ui.Secondary("Show me the lesson", "Open the lesson about this topic", 150);
            open.Click += (_, _) => _context.Show(lesson());
            row.Children.Add(open);
        }

        return row;
    }

    private Func<UserControl>? LessonFor(QuizTopic topic) => topic switch
    {
        QuizTopic.Chords or QuizTopic.Song => () => new UnderstandSongLesson(_context),
        QuizTopic.Bass => () => new BassLineLesson(_context),
        _ => null,
    };

    private UIElement Result()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"You got {_correct} of {_questions.Count}", FontSize = 20, FontWeight = FontWeights.SemiBold });
        var message = _correct == _questions.Count ? "Perfect round." : _correct >= _questions.Count * 0.7 ? "Solid. A few to revisit." : "Good start. Every question you missed came with a reason, and a new round gives new questions.";
        stack.Children.Add(new TextBlock { Text = message, FontSize = 13.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        if (_missed.Count > 0)
        {
            var topics = string.Join(", ", _missed.Distinct().Select(TheoryQuiz.TopicName));
            stack.Children.Add(new TextBlock { Text = $"Worth another look: {topics}.", FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }

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
