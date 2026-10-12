using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class GuidedCourseLesson : UserControl
{
    private readonly LearnContext _context;
    private readonly GuidedCourse _course;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private QuizQuestion? _fifth;
    private string? _fifthResult;

    public GuidedCourseLesson(LearnContext context, GuidedCourse course)
    {
        _context = context;
        _course = course;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Render();
            }
        };
        Render();
    }

    public static void StartStep(LearnContext context, GuidedCourse course, GuidedStep step)
    {
        GuidedProgress.SetLast(course, step);
        if (LearnCatalog.Find(step.Lesson) is { } card)
        {
            card.Launch(context);
        }
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All courses", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title(_course.Title));
        _page.Children.Add(Ui.Sub(_course.Blurb, 12));
        var done = GuidedProgress.DoneCount(_course);
        _page.Children.Add(new TextBlock { Text = done == _course.Steps.Count ? "Course finished. You can open any step again." : $"{done} of {_course.Steps.Count} steps done · about {_course.Minutes} minutes in all", FontSize = 12.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 10) });

        var current = GuidedProgress.NextStep(_course);
        for (var i = 0; i < _course.Steps.Count; i++)
        {
            var step = _course.Steps[i];
            _page.Children.Add(Row(i + 1, step, current == step));
            if (current == step && step.Key == "check:fifth")
            {
                _page.Children.Add(FifthCheck());
            }
        }

        _page.Children.Add(new TextBlock { Text = "A step is ticked when its check is passed. If a check does not suit you, mark the step as done yourself from its row. Your progress is saved on this computer.", FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
    }

    private UIElement Row(int number, GuidedStep step, bool current)
    {
        var done = GuidedProgress.IsDone(step);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = done ? Ui.GoodBrush : Brushes.Transparent,
            BorderBrush = done ? Ui.GoodBrush : current ? Ui.AccentBrush : Frozen(0x4A, 0x4A, 0x54),
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = done ? "✓" : "", FontSize = 13, Foreground = Ui.GoodText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        grid.Children.Add(mark);
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = $"{number} {step.Title}", FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = $"{step.Lesson} · {step.Minutes} min · check: {step.CheckText}", FontSize = 12, Foreground = done ? Frozen(0xCF, 0xE6, 0xD4) : current ? Frozen(0xD6, 0xD6, 0xDA) : Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (done)
        {
            right.Children.Add(new TextBlock { Text = "Done", FontSize = 12, Foreground = Frozen(0xCF, 0xE6, 0xD4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var again = new TextBlock { Text = "Open again", FontSize = 12, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            again.MouseLeftButtonUp += (_, _) => StartStep(_context, _course, step);
            right.Children.Add(again);
        }
        else
        {
            var manual = new TextBlock { Text = "Mark as done", FontSize = 12, Foreground = Ui.Res("TextDim"), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Tick this step without passing its check" };
            manual.MouseLeftButtonUp += (_, _) =>
            {
                GuidedProgress.MarkDone(step);
                Render();
            };
            right.Children.Add(manual);
            var start = current ? Ui.Accent("Start", "Open this lesson") : (UIElement)Ui.Secondary("Start", "Open this lesson", 60);
            if (start is Border accent)
            {
                accent.OnClick(() => StartStep(_context, _course, step));
            }
            else if (start is Button button)
            {
                button.Margin = new Thickness(0);
                button.Click += (_, _) => StartStep(_context, _course, step);
            }

            right.Children.Add(start);
        }

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return new Border
        {
            Background = done ? Frozen(0x1F, 0x33, 0x26) : current ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = done ? Ui.GoodBrush : current ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };
    }

    private UIElement FifthCheck()
    {
        var stack = new StackPanel();
        var key = _context.Main.DetectSongKeyQuick();
        if (key is null)
        {
            stack.Children.Add(new TextBlock { Text = "Your song has no key yet, so I cannot ask about it. Build a song first (Build a song), or mark this step as done yourself.", FontSize = 13, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            _fifth ??= TheoryQuiz.FifthOfKey(key, new Random());
            stack.Children.Add(new TextBlock { Text = "Check", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
            stack.Children.Add(new TextBlock { Text = _fifth.Text, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var options = new WrapPanel();
            for (var i = 0; i < _fifth.Options.Count; i++)
            {
                var index = i;
                var button = Ui.Secondary(_fifth.Options[i], null, 70);
                button.Height = 30;
                button.Click += (_, _) =>
                {
                    var question = _fifth!;
                    if (index == question.Correct)
                    {
                        GuidedProgress.Report("check:fifth", 1);
                        _fifthResult = null;
                        _fifth = null;
                    }
                    else
                    {
                        _fifthResult = $"Not quite. {question.Explanation} Try the new question.";
                        _fifth = null;
                    }

                    Render();
                };
                options.Children.Add(button);
            }

            stack.Children.Add(options);
            if (_fifthResult is not null)
            {
                stack.Children.Add(new TextBlock { Text = _fifthResult, FontSize = 12.5, Foreground = Frozen(0xE3, 0x6B, 0x6B), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            }
        }

        return new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(26, -2, 0, 8), Child = stack };
    }
}
