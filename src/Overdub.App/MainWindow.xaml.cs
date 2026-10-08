using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Overdub.Audio;

namespace Overdub.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            _viewModel.Open(args[1]);
        }

        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnSliderReset(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Slider slider && double.TryParse((string)slider.Tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            slider.Value = value;
        }
    }

    private void OnAddTrackClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        for (var i = 0; i < _viewModel.InputCount; i++)
        {
            var input = i;
            var item = new MenuItem { Header = $"Audio, input {input + 1}" };
            item.Click += (_, _) => _viewModel.AddAudioTrack(input);
            menu.Items.Add(item);
        }

        var midi = new MenuItem { Header = "MIDI keys (built-in synth)" };
        midi.Click += (_, _) => _viewModel.AddMidiTrack();
        menu.Items.Add(midi);
        menu.IsOpen = true;
    }

    private void OnNameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: TrackViewModel vm, Parent: Grid grid })
        {
            return;
        }

        vm.IsEditing = true;
        Dispatcher.BeginInvoke(() =>
        {
            var box = grid.Children.OfType<TextBox>().First();
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel vm })
        {
            vm.IsEditing = false;
        }
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TrackViewModel vm } box || e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        var binding = System.Windows.Data.BindingOperations.GetBindingExpression(box, TextBox.TextProperty);
        if (e.Key == Key.Enter)
        {
            binding?.UpdateSource();
        }
        else
        {
            binding?.UpdateTarget();
        }

        vm.IsEditing = false;
        e.Handled = true;
    }

    private void OnRemoveMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel vm })
        {
            vm.ResetRemove();
        }
    }

    private void OnSnapClick(object sender, RoutedEventArgs e) => _viewModel.CycleSnap();

    private void OnUndoClick(object sender, RoutedEventArgs e) => _viewModel.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => _viewModel.Redo();

    private void OnQuantizeClick(object sender, RoutedEventArgs e) => _viewModel.QuantizeSelection();

    private void OnQuantizeGridClick(object sender, RoutedEventArgs e) => _viewModel.CycleQuantizeGrid();

    private void OnSplitClick(object sender, RoutedEventArgs e) => _viewModel.SplitAtPlayhead();

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => _viewModel.DuplicateSelection();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _viewModel.DeleteSelection();

    private void OnKeyboardClick(object sender, RoutedEventArgs e) => new KeyboardWindow(_viewModel) { Owner = this }.Show();

    private void OnAnalyzeClick(object sender, RoutedEventArgs e) => new AnalysisWindow(_viewModel) { Owner = this }.Show();

    private void OnFileClick(object sender, RoutedEventArgs e)
    {
        var menu = MenuFor(sender);
        menu.Items.Add(Item("Open...", () => OnOpenClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Save", () => _viewModel.Save()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Import audio...", () => OnImportClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Export mixdown...", () => OnExportClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Export stems...", () => OnStemsClick(FileButton, new RoutedEventArgs())));
        menu.IsOpen = true;
    }

    private void OnToolsClick(object sender, RoutedEventArgs e)
    {
        var menu = MenuFor(sender);
        menu.Items.Add(Item("Tuner", () => OnTunerClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("On-screen keyboard", () => OnKeyboardClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Analyze clip", () => OnAnalyzeClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Measure latency", () => OnLatencyClick(ToolsButton, new RoutedEventArgs())));
        menu.IsOpen = true;
    }

    private static ContextMenu MenuFor(object sender) => new()
    {
        PlacementTarget = (UIElement)sender,
        Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
    };

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnTunerClick(object sender, RoutedEventArgs e) => new TunerWindow(_viewModel) { Owner = this }.Show();

    private void OnLatencyClick(object sender, RoutedEventArgs e) => new LatencyWindow(_viewModel) { Owner = this }.ShowDialog();

    private double? _rulerDragStart;

    private void OnRulerDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _viewModel.ClearRegion();
            return;
        }

        _rulerDragStart = e.GetPosition(Ruler).X;
        Ruler.CaptureMouse();
    }

    private void OnRulerMove(object sender, MouseEventArgs e)
    {
        if (_rulerDragStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            var x = e.GetPosition(Ruler).X;
            if (Math.Abs(x - start) >= 4)
            {
                _viewModel.SetRegion(start, x);
            }
        }
    }

    private void OnRulerUp(object sender, MouseButtonEventArgs e)
    {
        if (_rulerDragStart is not { } start)
        {
            return;
        }

        _rulerDragStart = null;
        Ruler.ReleaseMouseCapture();
        var x = e.GetPosition(Ruler).X;
        if (Math.Abs(x - start) < 4)
        {
            _viewModel.SeekToPixel(x);
        }
        else
        {
            _viewModel.SetRegion(start, x);
        }
    }

    private void OnTapClick(object sender, RoutedEventArgs e) => _viewModel.Tap();

    private void OnSignatureClick(object sender, RoutedEventArgs e) => _viewModel.CycleSignature();

    private void OnCountInClick(object sender, RoutedEventArgs e) => _viewModel.CycleCountIn();

    private void OnSaveClick(object sender, RoutedEventArgs e) => _viewModel.Save();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var recent = RecentProjects.Load();
        if (recent.Count == 0)
        {
            BrowseForProject();
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        foreach (var path in recent)
        {
            var item = new MenuItem
            {
                Header = $"{Path.GetFileName(Path.GetDirectoryName(path))}  ({File.GetLastWriteTime(path):d MMM HH:mm})",
                ToolTip = path,
            };
            item.Click += (_, _) => _viewModel.Open(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Browse..." };
        browse.Click += (_, _) => BrowseForProject();
        menu.Items.Add(browse);
        menu.IsOpen = true;
    }

    private void BrowseForProject()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Overdub project (project.overdub.json)|*.overdub.json",
            InitialDirectory = Directory.Exists(_viewModel.ProjectFolder) ? _viewModel.ProjectFolder : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Open(dialog.FileName);
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a backing track",
            Filter = "Audio files|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aif;*.aiff|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ImportAudio(dialog.FileName);
        }
    }

    private void OnStemsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for the stems",
            InitialDirectory = Directory.Exists(_viewModel.ProjectFolder) ? _viewModel.ProjectFolder : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportStems(dialog.FolderName);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav|MP3 audio (*.mp3)|*.mp3|FLAC audio (*.flac)|*.flac",
            FileName = Path.GetFileName(_viewModel.DefaultExportName),
            AddExtension = true,
            DefaultExt = ".wav",
            InitialDirectory = Path.GetDirectoryName(_viewModel.DefaultExportName),
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Export(dialog.FileName);
        }
    }

    private void OnLaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.Select(null);
        _viewModel.SeekToPixel(e.GetPosition(LaneArea).X);
    }
}
