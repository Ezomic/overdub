using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class SoundLibraryView : UserControl
{
    private static readonly string[] StatusNames = ["All packs", "Installed", "Not installed"];

    private readonly StackPanel _list = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<string> _busy = [];
    private readonly TextBox _search = new()
    {
        Padding = new Thickness(8, 5, 8, 5),
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
        FontSize = 13,
        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27)),
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)),
        CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)),
        ToolTip = "Search by name, kind or description",
    };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontSize = 13 };
    private readonly Button _kindButton = Ui.Secondary("All kinds", "Show one kind of instrument", 150);
    private readonly Button _statusButton = Ui.Secondary("All packs", "Show installed or not installed packs", 130);
    private MainViewModel? _main;
    private string? _kind;
    private int _status;

    public SoundLibraryView()
    {
        _count.Foreground = Ui.Res("TextDim");
        _kindButton.Margin = new Thickness(0, 0, 8, 0);
        _statusButton.Margin = new Thickness(0, 0, 8, 0);
        _kindButton.Height = 28;
        _statusButton.Height = 28;
        _kindButton.Click += (_, _) =>
        {
            var items = new List<(string Label, Action Pick)> { ("All kinds", () => _kind = null) };
            items.AddRange(SoundCatalog.Packs.Select(p => p.Kind).Distinct().Select(k => (k, (Action)(() => _kind = k))));
            ShowMenu(_kindButton, items);
        };
        _statusButton.Click += (_, _) => ShowMenu(_statusButton, StatusNames.Select((n, i) => (n, (Action)(() => _status = i))));
        _search.TextChanged += (_, _) => Refresh();

        var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_count, Dock.Right);
        DockPanel.SetDock(_statusButton, Dock.Right);
        DockPanel.SetDock(_kindButton, Dock.Right);
        filters.Children.Add(_count);
        filters.Children.Add(_statusButton);
        filters.Children.Add(_kindButton);
        filters.Children.Add(_search);

        var head = new StackPanel { Margin = new Thickness(20, 16, 20, 0) };
        head.Children.Add(Ui.Title("Sounds"));
        head.Children.Add(Ui.Sub("Free instruments you can install. Pick one from a track's Change sound button."));
        head.Children.Add(filters);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list, Padding = new Thickness(20, 0, 20, 10) });
        Content = root;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                RefreshFooter?.Invoke();
                Refresh();
            }
        };
    }

    private Action? RefreshFooter { get; set; }

    public void Attach(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    public void CancelDownloads() => _cancel.Cancel();

    private UIElement BuildFooter()
    {
        var quality = Ui.Secondary("Load quality: full", "How much of a big pack to load", 150);
        var cache = Ui.Secondary("Clear cache", "Delete the decoded samples; the next load decodes them again", 150);
        var qualityNote = new TextBlock { Foreground = Ui.Res("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var cacheNote = new TextBlock { Foreground = Ui.Res("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        void Show()
        {
            quality.Content = $"Load quality: {SampleSettings.QualityNames[SampleSettings.Quality].ToLowerInvariant()}";
            qualityNote.Text = SampleSettings.Describe(SampleSettings.Quality) + " Applies to sounds loaded from now on.";
            cache.Content = $"Clear cache ({SampleCache.SizeBytes() / 1073741824.0:0.0} GB)";
            cacheNote.Text = $"Decoded samples live in {SampleCache.Directory}, so a sound you have used before loads instantly.";
        }

        quality.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = quality, Placement = PlacementMode.Top };
            for (var i = 0; i < SampleSettings.QualityNames.Length; i++)
            {
                var pick = i;
                var item = new MenuItem { Header = SampleSettings.QualityNames[i], IsCheckable = true, IsChecked = i == SampleSettings.Quality, ToolTip = SampleSettings.Describe(i) };
                item.Click += (_, _) =>
                {
                    SampleSettings.Quality = pick;
                    Show();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        cache.Click += (_, _) =>
        {
            SampleCache.Clear();
            Show();
        };
        RefreshFooter = Show;
        Show();
        var grid = new Grid { Margin = new Thickness(20, 6, 20, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(quality);
        Grid.SetColumn(qualityNote, 1);
        grid.Children.Add(qualityNote);
        Grid.SetRow(cache, 1);
        cache.Margin = new Thickness(0, 6, 6, 0);
        grid.Children.Add(cache);
        Grid.SetRow(cacheNote, 1);
        Grid.SetColumn(cacheNote, 1);
        cacheNote.Margin = new Thickness(0, 6, 0, 0);
        grid.Children.Add(cacheNote);
        var about = new TextBlock { Text = "These sound packs come from the sfzinstruments collection on GitHub, mostly by Karoryfer Samples, under free licences (most are CC0). Each is one download, saved in Documents\\Overdub\\Instruments.", Foreground = Ui.Res("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 20, 14) };
        var stack = new StackPanel { Background = Ui.Res("Bg") };
        stack.Children.Add(new Border { Height = 1, Background = Ui.Res("Border") });
        stack.Children.Add(grid);
        stack.Children.Add(about);
        return stack;
    }

    private void ShowMenu(Button anchor, IEnumerable<(string Label, Action Pick)> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (label, pick) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                pick();
                _kindButton.Content = _kind ?? "All kinds";
                _statusButton.Content = StatusNames[_status];
                Refresh();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private bool Matches(SoundPack pack, string query, bool installed)
    {
        if (_kind is not null && pack.Kind != _kind)
        {
            return false;
        }

        if ((_status == 1 && !installed) || (_status == 2 && installed))
        {
            return false;
        }

        return query.Length == 0 || pack.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || pack.Kind.Contains(query, StringComparison.OrdinalIgnoreCase) || pack.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var query = _search.Text.Trim();
        var shown = 0;
        foreach (var pack in SoundCatalog.Packs)
        {
            var installed = SoundCatalog.IsInstalled(pack);
            if (!Matches(pack, query, installed))
            {
                continue;
            }

            shown++;
            _list.Children.Add(Card(pack, installed));
        }

        if (shown == 0)
        {
            _list.Children.Add(Ui.Sub("No packs match these filters.", 0));
        }

        _count.Text = $"{shown} of {SoundCatalog.Packs.Count}";
    }

    private UIElement Card(SoundPack pack, bool installed)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var head = new TextBlock { FontSize = 14 };
        head.Inlines.Add(new System.Windows.Documents.Run(pack.Name) { FontWeight = FontWeights.SemiBold });
        head.Inlines.Add(new System.Windows.Documents.Run($"   {pack.Kind} · {pack.SizeMb} MB") { Foreground = Ui.Res("TextDim"), FontSize = 12.5 });
        text.Children.Add(head);
        text.Children.Add(new TextBlock { Text = pack.Description, FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 12, 0) });
        var status = new TextBlock { Foreground = Ui.Res("TextDim"), FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        text.Children.Add(status);
        FrameworkElement action;
        if (installed)
        {
            action = Ui.Secondary("Installed", "Already on this computer", 100);
            ((Button)action).IsEnabled = false;
        }
        else if (_busy.Contains(pack.Id))
        {
            action = Ui.Secondary("Installing...", null, 100);
            ((Button)action).IsEnabled = false;
        }
        else
        {
            var install = Ui.Accent("Install", $"Download {pack.SizeMb} MB and unpack it");
            install.OnClick(async () => await Install(pack, install, status));
            action = install;
        }

        action.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(action, 1);
        grid.Children.Add(text);
        grid.Children.Add(action);
        return Ui.Card(grid, 8);
    }

    private async Task Install(SoundPack pack, Border button, TextBlock status)
    {
        if (!_busy.Add(pack.Id))
        {
            return;
        }

        button.Opacity = 0.5;
        status.Visibility = Visibility.Visible;
        var progress = new Progress<double>(p => status.Text = $"Downloading and unpacking: {p:P0}");
        try
        {
            await SoundCatalog.InstallAsync(pack, progress, _cancel.Token);
            SoundPrograms.Invalidate();
            _main?.ReportProblem("");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _main?.ReportProblem($"Could not install {pack.Name}: {ex.Message}");
        }
        finally
        {
            _busy.Remove(pack.Id);
        }

        if (IsLoaded)
        {
            RefreshFooter?.Invoke();
            Refresh();
        }
    }
}
