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

    public SoundLibraryWindow(MainViewModel main, Window owner)
    {
        Owner = owner;
        Icon = owner.Icon;
        Title = "Sound library";
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var intro = new TextBlock
        {
            Text = "Free sampled instruments from the sfzinstruments collection on GitHub, mostly by Karoryfer Samples, under free licences (most are CC0). Each one is a direct download of one zip file, saved into Documents\\Overdub\\Instruments. After installing, pick it from the sound menu of a guitar, bass or lead track, where it shows with (sampled). They take a few seconds to load the first time.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 18, 18, 10),
        };
        var root = new DockPanel();
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(18, 0, 18, 18) });
        Content = root;
        Closed += (_, _) => _cancel.Cancel();
        _main = main;
        Refresh();
    }

    private readonly MainViewModel _main;

    private void Refresh()
    {
        _list.Children.Clear();
        foreach (var pack in SoundCatalog.Packs)
        {
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
    }
}
