using System.Windows;
using System.Windows.Controls;
using Overdub.Audio;

namespace Overdub.App;

public partial class AnalysisWindow : Window
{
    private readonly MainViewModel _viewModel;

    public AnalysisWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await Run();
    }

    private async Task Run()
    {
        var report = await _viewModel.AnalyzeSelectionAsync();
        Heading.Text = report.Title;
        Note.Text = report.Message;
        if (report.Tempo is { } tempo)
        {
            TempoLine.Text = $"Tempo: about {tempo.Bpm:0.#} BPM (confidence {tempo.Confidence:P0}). If it feels like half or double time, {tempo.Alternative:0.#} BPM.";
            AddTempoButton(tempo.Bpm);
            AddTempoButton(tempo.Alternative);
        }

        if (report.Key is { } key)
        {
            KeyLine.Text = $"Key: {key.Name} (confidence {key.Confidence:P0}, next best {key.Alternative}).";
        }
    }

    private void AddTempoButton(double bpm)
    {
        var button = new Button
        {
            Content = $"Set project to {bpm:0.#} BPM",
            Style = (Style)FindResource("TransportButton"),
            Width = double.NaN,
            Height = 26,
            Padding = new Thickness(12, 0, 12, 0),
        };
        button.Click += (_, _) => _viewModel.Bpm = Math.Round(bpm);
        TempoButtons.Children.Add(button);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
