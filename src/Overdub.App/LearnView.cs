using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed record LearnCard(string Title, string Description, bool IsNew, Action<MainViewModel, Window> Open);

public static class LearnCatalog
{
    public static LearnCard StartHere { get; } = new(
        "Start here: see your song on the bass neck",
        "Watch the notes of each chord light up on the bass neck while the song plays, and see where the key lives.",
        false,
        (main, owner) => OpenPractice(main, owner));

    public static IReadOnlyList<LearnCard> Cards { get; } =
    [
        new("Bass coach", "Bass lines over your chords, shown as tab, to learn and play along with.", false, (main, owner) => OpenCoach(main, owner)),
        new("Tuner", "Tune your bass or guitar before you play, with a needle that shows how close you are.", false, (main, owner) => new TunerWindow(main) { Owner = owner }.Show()),
    ];

    private static TrackViewModel? Bass(MainViewModel main) => main.Tracks.FirstOrDefault(t => t.Model.Machine == MachineRole.Bass);

    private static void OpenPractice(MainViewModel main, Window owner)
    {
        if (Bass(main) is { } bass)
        {
            new BassPracticeWindow(main, owner, bass.Model).Show();
        }
        else
        {
            main.ShowMessage("Add a bass machine first: go to Build a song and use Add a player.");
        }
    }

    private static void OpenCoach(MainViewModel main, Window owner)
    {
        if (Bass(main) is { } bass)
        {
            new BassCoachWindow(main, owner, bass.Model).Show();
        }
        else
        {
            main.ShowMessage("Add a bass machine first: go to Build a song and use Add a player.");
        }
    }
}

public sealed class LearnView : UserControl
{
    private readonly StackPanel _page = new() { Margin = new Thickness(20, 16, 20, 20) };
    private MainViewModel? _main;

    public LearnView()
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
            }
        };
    }

    public Action<int>? Navigate { get; set; }

    public void Attach(MainViewModel main)
    {
        _main = main;
        main.EditHistoryChanged += () =>
        {
            if (IsVisible)
            {
                Refresh();
            }
        };
        Refresh();
    }

    private Window? Owner => Window.GetWindow(this);

    private void Refresh()
    {
        if (_main is null)
        {
            return;
        }

        _page.Children.Clear();
        _page.Children.Add(Ui.Title("Learn"));
        _page.Children.Add(Ui.Sub("Short, hands-on lessons that use the song you are working on."));
        _page.Children.Add(SongPanel());
        _page.Children.Add(StartCard());
        var grid = new UniformGrid { Columns = 3 };
        foreach (var card in LearnCatalog.Cards)
        {
            grid.Children.Add(Card(card));
        }

        _page.Children.Add(grid);
    }

    private UIElement SongPanel()
    {
        var key = _main!.DetectSongKeyQuick();
        var progression = _main.SongProgression();
        var row = new DockPanel();
        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = "Your song", FontSize = 15, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = "The lessons below use this", FontSize = 12.5, Foreground = Ui.Res("TextDim") });
        DockPanel.SetDock(left, Dock.Left);
        row.Children.Add(left);
        if (key is null && progression.Count == 0)
        {
            var go = Ui.Secondary("Build a song", "Start from a template or add players", 120);
            go.Click += (_, _) => Navigate?.Invoke(1);
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            row.Children.Add(new TextBlock { Text = "There is no song yet. Start one in Build a song, then come back.", FontSize = 13, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center });
            return Wrap(row);
        }

        var play = Ui.Secondary("Play it", "Play the song from the start", 90);
        play.Click += (_, _) =>
        {
            _main.Engine.Seek(0);
            if (!_main.Engine.IsPlaying)
            {
                _main.PlayCommand.Execute(null);
            }
        };
        DockPanel.SetDock(play, Dock.Right);
        row.Children.Add(play);
        var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (key is not null)
        {
            pills.Children.Add(Ui.Pill($"Key: {key.Name}"));
        }

        if (progression.Count > 0)
        {
            pills.Children.Add(Ui.Pill("Chords: " + string.Join(" · ", progression.Take(8).Select(c => c.Name))));
        }

        row.Children.Add(pills);
        return Wrap(row);
    }

    private static Border Wrap(UIElement child)
    {
        var border = Ui.Card(child, 14);
        border.Padding = new Thickness(14, 10, 14, 10);
        return border;
    }

    private UIElement StartCard()
    {
        var start = LearnCatalog.StartHere;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = start.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = start.Description, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xC6, 0xB4)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 12, 0) });
        var open = Ui.Accent("Open", "Open this lesson");
        open.OnClick(() => start.Open(_main!, Owner!));
        Grid.SetColumn(open, 1);
        grid.Children.Add(text);
        grid.Children.Add(open);
        return new Border
        {
            Background = Ui.AccentTint,
            BorderBrush = Ui.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 14),
            Child = grid,
        };
    }

    private UIElement Card(LearnCard card)
    {
        var text = new StackPanel();
        var head = new DockPanel();
        if (card.IsNew)
        {
            var badge = new Border { Background = Ui.GoodBrush, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Child = new TextBlock { Text = "New", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.GoodText } };
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }

        head.Children.Add(new TextBlock { Text = card.Title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(head);
        text.Children.Add(new TextBlock { Text = card.Description, FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 17 });
        var border = Ui.ClickCard(text, () => card.Open(_main!, Owner!), card.Description);
        border.Margin = new Thickness(0, 0, 10, 10);
        border.Padding = new Thickness(13, 11, 13, 11);
        return border;
    }
}
