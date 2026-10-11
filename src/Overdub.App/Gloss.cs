using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public static class Gloss
{
    private static readonly Dictionary<string, GlossaryTerm> ByAlias = Glossary.Aliases()
        .GroupBy(a => a.Alias.ToLowerInvariant())
        .ToDictionary(g => g.Key, g => g.First().Term);

    private static readonly Regex Pattern = new(
        @"\b(" + string.Join("|", Glossary.Aliases().Select(a => Regex.Escape(a.Alias))) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Brush TermBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xDC, 0xB8)));

    private static readonly TextDecorationCollection Dotted = CreateDotted();

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static TextDecorationCollection CreateDotted()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0xA2, 0x3C)), 1) { DashStyle = DashStyles.Dot };
        pen.Freeze();
        var collection = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.Pixel, TextDecorationUnit.FontRecommended) };
        collection.Freeze();
        return collection;
    }

    public static IReadOnlyList<Inline> Inlines(string text, LearnContext context)
    {
        var result = new List<Inline>();
        var seen = new HashSet<string>();
        var last = 0;
        foreach (Match match in Pattern.Matches(text))
        {
            if (!ByAlias.TryGetValue(match.Value.ToLowerInvariant(), out var term) || !seen.Add(term.Name))
            {
                continue;
            }

            if (match.Index > last)
            {
                result.Add(new Run(text[last..match.Index]));
            }

            var chosen = term;
            var run = new Run(match.Value)
            {
                Foreground = TermBrush,
                TextDecorations = Dotted,
                Cursor = Cursors.Hand,
                ToolTip = Card(term),
            };
            ToolTipService.SetInitialShowDelay(run, 250);
            ToolTipService.SetShowDuration(run, 30000);
            run.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                context.OpenGlossary(chosen.Name);
            };
            result.Add(run);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            result.Add(new Run(text[last..]));
        }

        return result;
    }

    public static TextBlock Glossed(this TextBlock block, LearnContext context)
    {
        var text = block.Text;
        block.Inlines.Clear();
        block.Inlines.AddRange(Inlines(text, context));
        return block;
    }

    private static ToolTip Card(GlossaryTerm term)
    {
        var stack = new StackPanel { MaxWidth = 300 };
        stack.Children.Add(new TextBlock { Text = term.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(new TextBlock { Text = term.Definition, FontSize = 12.5, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        if (term.Example is not null)
        {
            stack.Children.Add(new TextBlock { Text = $"Example: {term.Example}", FontSize = 12.5, LineHeight = 18, Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xE6, 0xD4)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        stack.Children.Add(new TextBlock { Text = "Click the word to open it in the glossary ›", FontSize = 12.5, Foreground = Ui.AccentBrush });
        return new ToolTip
        {
            Content = stack,
            Background = Ui.Res("Panel"),
            BorderBrush = Ui.AccentBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Foreground = Ui.Res("Text"),
        };
    }
}
