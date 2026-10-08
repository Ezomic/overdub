using System.IO;
using System.Windows;
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
