using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav",
            FileName = Path.GetFileName(_viewModel.DefaultExportName),
            InitialDirectory = Path.GetDirectoryName(_viewModel.DefaultExportName),
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Export(dialog.FileName);
        }
    }

    private void OnLaneMouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.SeekToPixel(e.GetPosition(LaneArea).X);
}
