using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class MixCheckLesson : UserControl
{
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private MixReport? _report;
    private string? _problem;
    private bool _running;
    private DateTime _checkedAt;

    public MixCheckLesson(LearnContext context)
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

    private static readonly Brush Wrong = Frozen(0xE3, 0x6B, 0x6B);

    private static Brush LightBrush(MixLight light) => light switch
    {
        MixLight.Fine => Ui.GoodBrush,
        MixLight.Look => Ui.AccentBrush,
        _ => Wrong,
    };

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Mix check"));
        _page.Children.Add(Ui.Sub("Overdub plays your song in the background, measures the mix, and tells you in plain words what to look at. It never changes anything by itself.", 12));

        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var go = Ui.Accent(_running ? "Checking..." : "Check my mix", "Render the song and measure it");
        go.Margin = new Thickness(0, 0, 12, 6);
        go.OnClick(() => _ = RunAsync());
        row.Children.Add(go);
        var status = _running
            ? "Checking: this can take up to half a minute."
            : _problem ?? (_report is null ? "" : $"Checked at {_checkedAt:HH:mm} · whole song, {_report.Bars} bars · took {_report.Seconds:0} seconds");
        row.Children.Add(new TextBlock { Text = status, FontSize = 12.5, Foreground = _problem is null ? Ui.Res("TextDim") : Wrong, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        _page.Children.Add(row);

        if (_report is null)
        {
            return;
        }

        _page.Children.Add(Summary(_report));
        foreach (var finding in _report.Findings)
        {
            _page.Children.Add(Card(finding));
        }

        _page.Children.Add(new TextBlock { Text = "These are guidelines from measurement, not rules. If you like how it sounds, it sounds right.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
    }

    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _problem = null;
        Render();
        try
        {
            var session = _context.Main.MixSession;
            _report = await Task.Run(() => MixCheck.Run(session));
            _checkedAt = DateTime.Now;
        }
        catch (Exception e)
        {
            _problem = e.Message.Contains("Nothing to export", StringComparison.OrdinalIgnoreCase) ? "There is nothing to measure yet. Build a song first (Build a song), then check it." : e.Message;
            _report = null;
        }
        finally
        {
            _running = false;
        }

        Render();
    }

    private UIElement Summary(MixReport report)
    {
        var row = new DockPanel();
        var score = new TextBlock { FontSize = 34, FontWeight = FontWeights.SemiBold, Foreground = Ui.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        score.Inlines.Add(new System.Windows.Documents.Run($"{report.Fine}"));
        score.Inlines.Add(new System.Windows.Documents.Run($" of {report.Findings.Count}") { FontSize = 18, Foreground = Ui.Res("TextDim") });
        DockPanel.SetDock(score, Dock.Left);
        row.Children.Add(score);
        row.Children.Add(new TextBlock { Text = report.Summary, FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var card = Ui.Card(row, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        return card;
    }

    private UIElement Card(MixFinding finding)
    {
        var stack = new StackPanel();
        var head = new DockPanel();
        var value = new TextBlock { Text = finding.Value, FontSize = 13, Foreground = Frozen(0xD6, 0xD6, 0xDA), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0), TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        var title = new TextBlock { FontSize = 15 };
        title.Inlines.Add(new System.Windows.Documents.Run(finding.Title) { FontWeight = FontWeights.SemiBold });
        title.Inlines.Add(new System.Windows.Documents.Run($"  {finding.Label}") { FontSize = 12, Foreground = LightBrush(finding.Light) });
        head.Children.Add(title);
        stack.Children.Add(head);

        if (finding.Title == "Loudness" && finding.Bar is { } bar)
        {
            stack.Children.Add(LoudnessBar(bar));
        }
        else if (finding.Title == "Low end" && finding.Parts is { Count: > 0 } shares)
        {
            stack.Children.Add(Shares(shares));
        }
        else if (finding.Title == "Who is loudest" && finding.Parts is { Count: > 0 } loudest)
        {
            stack.Children.Add(LoudestBars(loudest));
        }

        stack.Children.Add(new TextBlock { Text = finding.Text, FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }.Glossed(_context));
        if (finding.Step is { } step)
        {
            var link = new TextBlock { Text = $"Open Mixing basics, step {(int)step + 1} ›", FontSize = 12.5, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 5, 0, 0) };
            link.MouseLeftButtonUp += (_, _) =>
            {
                GuidedProgress.Report("opened:Mixing basics", 1);
                _context.Show(new MixingLesson(_context, step));
            };
            stack.Children.Add(link);
        }

        return new Border
        {
            Background = Ui.Res("Panel"),
            BorderBrush = Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 11, 16, 11),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new Border { BorderBrush = LightBrush(finding.Light), BorderThickness = new Thickness(4, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(-16, -11, -16, -11), Child = new Border { Padding = new Thickness(0, 11, 16, 11), Child = stack } },
        };
    }

    private UIElement LoudnessBar(double position)
    {
        var grid = new Grid { Height = 16, Margin = new Thickness(0, 8, 0, 2) };
        var track = new Border { Height = 8, Background = Ui.Res("Well"), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(track);
        var fill = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 8 };
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, position), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - position), GridUnitType.Star) });
        fill.Children.Add(new Border { Background = Ui.AccentBrush, CornerRadius = new CornerRadius(4) });
        grid.Children.Add(fill);
        var marker = new Grid();
        var target = (MixCheck.StreamingLufs + 30) / 24;
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(target, GridUnitType.Star) });
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - target, GridUnitType.Star) });
        var line = new Border { Width = 2, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right };
        marker.Children.Add(line);
        grid.Children.Add(marker);
        var holder = new StackPanel();
        holder.Children.Add(grid);
        var labels = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var louder = new TextBlock { Text = "louder", FontSize = 11.5, Foreground = Ui.Res("TextDim") };
        DockPanel.SetDock(louder, Dock.Right);
        labels.Children.Add(louder);
        labels.Children.Add(new TextBlock { Text = "quieter", FontSize = 11.5, Foreground = Ui.Res("TextDim") });
        labels.Children.Add(new TextBlock { Text = "white marker: streaming target, -14 LUFS", FontSize = 11.5, Foreground = Ui.Res("TextDim"), HorizontalAlignment = HorizontalAlignment.Center });
        holder.Children.Add(labels);
        return holder;
    }

    private static UIElement Shares(IReadOnlyList<MixPart> parts)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
        var bar = new Grid { Height = 22 };
        for (var i = 0; i < parts.Count; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, parts[i].Value), GridUnitType.Star) });
            var cell = new Border { Background = new SolidColorBrush(MixBarsControl.RoleColor(parts[i].Role)), Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), ClipToBounds = true, Child = new TextBlock { Text = parts[i].Name, FontSize = 11.5, Foreground = Frozen(0x10, 0x13, 0x0F), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
            Grid.SetColumn(cell, i);
            bar.Children.Add(cell);
        }

        holder.Children.Add(bar);
        holder.Children.Add(new TextBlock { Text = "Share of the energy below 200 Hz", FontSize = 11.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 3, 0, 0) });
        return holder;
    }

    private static UIElement LoudestBars(IReadOnlyList<MixPart> parts)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
        foreach (var part in parts)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = part.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = Ui.Res("Well"), CornerRadius = new CornerRadius(4) });
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, part.Value), GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - part.Value), GridUnitType.Star) });
            fill.Children.Add(new Border { Background = new SolidColorBrush(MixBarsControl.RoleColor(part.Role)), CornerRadius = new CornerRadius(4) });
            track.Children.Add(fill);
            Grid.SetColumn(track, 1);
            row.Children.Add(track);
            holder.Children.Add(row);
        }

        return holder;
    }
}
