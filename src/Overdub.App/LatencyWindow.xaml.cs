using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Overdub.App;

public partial class LatencyWindow : Window
{
    private readonly MainViewModel _viewModel;
    private int _input;

    public LatencyWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        for (var i = 0; i < viewModel.AudioInputCount; i++)
        {
            var index = i;
            var chip = new ToggleButton
            {
                Content = $"Input {i + 1}",
                Style = (Style)FindResource("Chip"),
                IsChecked = i == 0,
                Focusable = false,
            };
            chip.Click += (_, _) => SelectInput(index);
            InputChips.Children.Add(chip);
        }

        Result.Text = viewModel.LatencySummary;
    }

    private void SelectInput(int index)
    {
        _input = index;
        for (var i = 0; i < InputChips.Children.Count; i++)
        {
            ((ToggleButton)InputChips.Children[i]).IsChecked = i == index;
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
        Result.Text = "Measuring...";
        Result.Text = await _viewModel.MeasureLatencyAsync(_input);
        StartButton.IsEnabled = true;
        ResetButton.IsEnabled = true;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetLatency();
        Result.Text = _viewModel.LatencySummary;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
