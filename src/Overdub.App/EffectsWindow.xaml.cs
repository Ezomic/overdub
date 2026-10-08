using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Overdub.App;

public partial class EffectsWindow : Window
{
    private readonly TrackViewModel _track;
    private readonly List<ToggleButton> _toggles = [];
    private readonly List<(int Effect, int Parameter, Slider Slider, TextBlock Readout)> _sliders = [];
    private bool _syncing;

    public EffectsWindow(TrackViewModel track)
    {
        _track = track;
        InitializeComponent();
        Title = $"Effects: {track.Name}";
        Build();
        _track.EffectsChanged += Sync;
        Closed += (_, _) => _track.EffectsChanged -= Sync;
        Sync();
    }

    private void Build()
    {
        var effects = _track.Effects.Effects;
        for (var e = 0; e < effects.Count; e++)
        {
            var effect = effects[e];
            var index = e;
            var header = new DockPanel { Margin = new Thickness(0, e == 0 ? 0 : 20, 0, 8) };
            var toggle = new ToggleButton { Content = "On", Style = (Style)FindResource("Chip"), Focusable = false, Margin = new Thickness(0) };
            DockPanel.SetDock(toggle, Dock.Right);
            void Changed(object sender, RoutedEventArgs args)
            {
                if (!_syncing)
                {
                    _track.SetEffectEnabled(index, toggle.IsChecked == true);
                }
            }

            toggle.Checked += Changed;
            toggle.Unchecked += Changed;
            _toggles.Add(toggle);
            header.Children.Add(toggle);
            header.Children.Add(new TextBlock { Text = effect.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Panels.Children.Add(header);

            for (var p = 0; p < effect.Parameters.Count; p++)
            {
                var parameter = effect.Parameters[p];
                var slotIndex = p;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.Children.Add(new TextBlock { Text = parameter.Name, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center });
                var slider = new Slider
                {
                    Minimum = parameter.Min,
                    Maximum = parameter.Max,
                    Value = parameter.Default,
                    IsSnapToTickEnabled = parameter.Integer,
                    TickFrequency = parameter.Integer ? 1 : 0.01,
                    Focusable = false,
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(slider, 1);
                var readout = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
                Grid.SetColumn(readout, 2);
                slider.ValueChanged += (_, args) =>
                {
                    if (!_syncing)
                    {
                        _track.SetEffectValue(index, slotIndex, args.NewValue);
                    }
                };
                slider.MouseDoubleClick += (_, _) => _track.SetEffectValue(index, slotIndex, parameter.Default);
                row.Children.Add(slider);
                row.Children.Add(readout);
                _sliders.Add((e, p, slider, readout));
                Panels.Children.Add(row);
            }
        }
    }

    private void Sync()
    {
        _syncing = true;
        var effects = _track.Effects.Effects;
        for (var e = 0; e < effects.Count; e++)
        {
            _toggles[e].IsChecked = effects[e].Enabled;
        }

        foreach (var (effect, parameter, slider, readout) in _sliders)
        {
            var value = effects[effect].Get(parameter);
            slider.Value = value;
            var spec = effects[effect].Parameters[parameter];
            readout.Text = spec.Integer ? $"{value:0}" : spec.Unit.Length == 0 ? $"{value:0.00}" : $"{value:0.#} {spec.Unit}";
        }

        _syncing = false;
    }
}
