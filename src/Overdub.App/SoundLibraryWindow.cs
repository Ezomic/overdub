using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class SoundLibraryWindow : Window
{
    private readonly StackPanel _list = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<string> _busy = [];
    private readonly TextBox _search = new() { Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27)), Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)), CaretBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA)) };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private string? _kind;
    private int _status;

    public SoundLibraryWindow(MainViewModel main, Window owner)
    {
        Owner = owner;
        Icon = owner.Icon;
        Title = "Sound library";
        Width = 640;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var intro = new TextBlock
        {
            Text = "Free sampled instruments from the sfzinstruments collection on GitHub, mostly by Karoryfer Samples, under free licences (most are CC0). Each one is a direct download of one zip file, saved into Documents\\Overdub\\Instruments. After installing, pick it from the sound button of a guitar, bass, lead or keys track. Pianos, organs, horns and strings show up on the keys and lead tracks. They take a few seconds to load the first time.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 18, 18, 10),
        };
        var quality = new DockPanel { Margin = new Thickness(18, 0, 18, 10) };
        var qualityButton = new Button { Style = (Style)FindResource("TransportButton"), Width = 150, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Focusable = false, Margin = new Thickness(0, 0, 10, 0) };
        var qualityNote = new TextBlock { Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        void ShowQuality()
        {
            qualityButton.Content = $"Load quality: {SampleSettings.QualityNames[SampleSettings.Quality].ToLowerInvariant()}";
            qualityNote.Text = SampleSettings.Describe(SampleSettings.Quality) + " Applies to sounds loaded from now on.";
        }

        qualityButton.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = qualityButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            for (var i = 0; i < SampleSettings.QualityNames.Length; i++)
            {
                var pick = i;
                var item = new MenuItem { Header = SampleSettings.QualityNames[i], IsCheckable = true, IsChecked = i == SampleSettings.Quality, ToolTip = SampleSettings.Describe(i) };
                item.Click += (_, _) =>
                {
                    SampleSettings.Quality = pick;
                    ShowQuality();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        ShowQuality();
        DockPanel.SetDock(qualityButton, Dock.Left);
        quality.Children.Add(qualityButton);
        quality.Children.Add(qualityNote);
        var cache = new DockPanel { Margin = new Thickness(18, 0, 18, 10) };
        var clearCache = new Button { Content = "Clear cache", Style = (Style)FindResource("TransportButton"), Width = 150, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Focusable = false, Margin = new Thickness(0, 0, 10, 0) };
        var cacheNote = new TextBlock { Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        void ShowCache() => cacheNote.Text = $"Decoded samples are kept in a cache ({SampleCache.SizeBytes() / 1048576.0:0} MB in {SampleCache.Directory}) so a sound loads instantly after its first use. Clearing it only costs the next load.";
        clearCache.Click += (_, _) =>
        {
            SampleCache.Clear();
            ShowCache();
        };
        ShowCache();
        DockPanel.SetDock(clearCache, Dock.Left);
        cache.Children.Add(clearCache);
        cache.Children.Add(cacheNote);
        var filters = new DockPanel { Margin = new Thickness(18, 0, 18, 10) };
        var kindButton = FilterButton("All kinds", 150);
        var statusButton = FilterButton("All packs", 130);
        _count.Foreground = (Brush)FindResource("TextDim");
        void ShowFilters()
        {
            kindButton.Content = _kind ?? "All kinds";
            statusButton.Content = StatusNames[_status];
        }

        kindButton.Click += (_, _) =>
        {
            var items = new List<(string Label, Action Pick)> { ("All kinds", () => _kind = null) };
            items.AddRange(SoundCatalog.Packs.Select(p => p.Kind).Distinct().Select(k => (k, (Action)(() => _kind = k))));
            ShowMenu(kindButton, items, () => { ShowFilters(); Refresh(); });
        };
        statusButton.Click += (_, _) => ShowMenu(statusButton, StatusNames.Select((n, i) => (n, (Action)(() => _status = i))), () => { ShowFilters(); Refresh(); });
        _search.TextChanged += (_, _) => Refresh();
        DockPanel.SetDock(_count, Dock.Right);
        DockPanel.SetDock(statusButton, Dock.Right);
        DockPanel.SetDock(kindButton, Dock.Right);
        filters.Children.Add(_count);
        filters.Children.Add(statusButton);
        filters.Children.Add(kindButton);
        filters.Children.Add(_search);
        var root = new DockPanel();
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(quality, Dock.Top);
        DockPanel.SetDock(cache, Dock.Top);
        DockPanel.SetDock(filters, Dock.Top);
        root.Children.Add(intro);
        root.Children.Add(quality);
        root.Children.Add(cache);
        root.Children.Add(filters);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(18, 0, 18, 18) });
        Content = root;
        Closed += (_, _) => _cancel.Cancel();
        _main = main;
        Refresh();
    }

    private readonly MainViewModel _main;

    private static readonly string[] StatusNames = ["All packs", "Installed", "Not installed"];

    private Button FilterButton(string text, double width) => new()
    {
        Content = text,
        Style = (Style)FindResource("TransportButton"),
        Width = width,
        Height = 28,
        FontSize = 12,
        Padding = new Thickness(12, 0, 12, 0),
        Focusable = false,
        Margin = new Thickness(0, 0, 0, 0),
    };

    private static void ShowMenu(Button anchor, IEnumerable<(string Label, Action Pick)> items, Action after)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, pick) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                pick();
                after();
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
            if (!Matches(pack, query, SoundCatalog.IsInstalled(pack)))
            {
                continue;
            }

            shown++;
            var current = pack;
            var card = new Border
            {
                Background = (Brush)FindResource("Panel"),
                BorderBrush = (Brush)FindResource("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{current.Name}  ({current.Kind}, {current.SizeMb} MB)", FontSize = 15, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = current.Description, Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 12, 0) });
            var status = new TextBlock { Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 6, 0, 0) };
            text.Children.Add(status);
            var installed = SoundCatalog.IsInstalled(current);
            var button = new Button
            {
                Content = installed ? "Installed" : _busy.Contains(current.Id) ? "Installing..." : "Install",
                IsEnabled = !installed && !_busy.Contains(current.Id),
                Style = (Style)FindResource("TransportButton"),
                Width = 100,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Top,
            };
            button.Click += async (_, _) =>
            {
                _busy.Add(current.Id);
                button.IsEnabled = false;
                button.Content = "Installing...";
                var progress = new Progress<double>(p => status.Text = $"Downloading and unpacking: {p:P0}");
                try
                {
                    await SoundCatalog.InstallAsync(current, progress, _cancel.Token);
                    _main.ReportProblem("");
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _main.ReportProblem($"Could not install {current.Name}: {ex.Message}");
                }
                finally
                {
                    _busy.Remove(current.Id);
                }

                if (IsLoaded)
                {
                    Refresh();
                }
            };
            Grid.SetColumn(button, 1);
            grid.Children.Add(text);
            grid.Children.Add(button);
            card.Child = grid;
            _list.Children.Add(card);
        }

        if (shown == 0)
        {
            _list.Children.Add(new TextBlock { Text = "No packs match these filters.", Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 10, 0, 0) });
        }

        _count.Text = $"{shown} of {SoundCatalog.Packs.Count}";
    }
}
