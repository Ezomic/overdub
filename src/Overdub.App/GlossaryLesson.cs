using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class GlossaryLesson : UserControl
{
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly string? _highlight;
    private string? _category;
    private string _query = "";
    private StackPanel _terms = new();
    private TextBlock _count = new();

    public GlossaryLesson(LearnContext context, string? highlight)
    {
        _context = context;
        _highlight = highlight;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        Build();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void Build()
    {
        var back = new TextBlock { Text = "‹ Back", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Glossary"));
        _page.Children.Add(Ui.Sub("Plain-English meanings for the words used in Overdub and in music.", 10));

        var search = new TextBox
        {
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)),
            ToolTip = "Search for a word or part of its meaning",
        };
        var hint = new TextBlock { Text = "Search a word, such as fifth or compression", FontSize = 13, Foreground = Ui.Res("TextDim"), IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var box = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        box.Children.Add(search);
        box.Children.Add(hint);
        search.TextChanged += (_, _) =>
        {
            _query = search.Text.Trim();
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill();
        };
        _page.Children.Add(box);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var all = Chip("All", true, chips);
        chips.Children.Add(all);
        foreach (var category in Glossary.Categories)
        {
            chips.Children.Add(Chip(category, false, chips));
        }

        _page.Children.Add(chips);
        _page.Children.Add(_terms);
        _count = new TextBlock { FontSize = 12.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 4, 0, 0) };
        _page.Children.Add(_count);
        Fill();
        if (_highlight is not null)
        {
            Loaded += (_, _) => ((Panel)_terms.Children[0]).Children.OfType<FrameworkElement>().FirstOrDefault(c => (c.Tag as string) == _highlight)?.BringIntoView();
        }
    }

    private Border Chip(string text, bool selected, WrapPanel chips)
    {
        var chip = new Border { Cursor = Cursors.Hand, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = text, FontSize = 13 } };
        Style(chip, selected);
        chip.OnClick(() =>
        {
            _category = text == "All" ? null : text;
            foreach (var other in chips.Children.OfType<Border>())
            {
                Style(other, ReferenceEquals(other, chip));
            }

            Fill();
        });
        return chip;
    }

    private static void Style(Border chip, bool selected)
    {
        chip.Background = selected ? Ui.AccentTint : Ui.Res("Panel");
        chip.BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border");
    }

    private void Fill()
    {
        _terms.Children.Clear();
        var matches = Glossary.All
            .Where(t => (_category is null || t.Category == _category)
                && (_query.Length == 0 || t.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) || t.Definition.Contains(_query, StringComparison.OrdinalIgnoreCase) || t.Aliases.Any(a => a.Contains(_query, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var term in matches)
        {
            grid.Children.Add(Card(term));
        }

        _terms.Children.Add(grid);
        _count.Text = matches.Count == 0
            ? "No term matches that. Try a shorter word."
            : $"Showing {matches.Count} of {Glossary.All.Count} terms. Many link to the lesson that uses them.";
    }

    private UIElement Card(GlossaryTerm term)
    {
        var stack = new StackPanel();
        var head = new TextBlock { FontSize = 14 };
        head.Inlines.Add(new System.Windows.Documents.Run(term.Name) { FontWeight = FontWeights.SemiBold });
        head.Inlines.Add(new System.Windows.Documents.Run($"   {term.Category}") { FontSize = 11.5, Foreground = Ui.Res("TextDim") });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock { Text = term.Definition, FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (term.Example is not null)
        {
            stack.Children.Add(new TextBlock { Text = $"Example: {term.Example}", FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xE6, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        if (term.Lesson is { } lesson && LearnCatalog.Find(lesson) is { } card)
        {
            var link = new TextBlock { Text = $"Open the lesson: {lesson} ›", FontSize = 12.5, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            link.MouseLeftButtonUp += (_, _) => card.Launch(_context);
            stack.Children.Add(link);
        }

        var highlighted = string.Equals(term.Name, _highlight, StringComparison.OrdinalIgnoreCase);
        return new Border
        {
            Background = highlighted ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = highlighted ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 8, 8),
            Tag = term.Name,
            Child = stack,
        };
    }
}
