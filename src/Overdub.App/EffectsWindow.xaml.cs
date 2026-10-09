using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Overdub.App;

public partial class EffectsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly TrackViewModel _track;
    private TextBlock _pluginName = null!;
    private Button _pluginEdit = null!;
    private ToggleButton _pluginOn = null!;
    private Button _pluginClear = null!;
    private readonly List<ToggleButton> _toggles = [];
    private readonly List<(int Effect, int Parameter, Slider Slider, TextBlock Readout)> _sliders = [];
    private bool _syncing;

    public EffectsWindow(MainViewModel main, TrackViewModel track)
    {
        _main = main;
        _track = track;
        InitializeComponent();
        Title = track.Model is { IsMidi: true, Machine: null } ? $"Instrument: {track.Name}" : $"Effects: {track.Name}";
        Build();
        _track.EffectsChanged += Sync;
        Closed += (_, _) => _track.EffectsChanged -= Sync;
        Sync();
    }

    private void BuildPluginRow()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _pluginOn = new ToggleButton { Content = "On", Style = (Style)FindResource("Chip"), Focusable = false, Margin = new Thickness(0) };
        DockPanel.SetDock(_pluginOn, Dock.Right);
        _pluginOn.Click += (_, _) => _main.SetPluginEnabled(_track, _pluginOn.IsChecked == true);
        header.Children.Add(_pluginOn);
        header.Children.Add(new TextBlock { Text = _track.Model.IsMidi ? "VST3 instrument" : "VST3 plugin", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Panels.Children.Add(header);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        var choose = new Button { Content = "Choose...", Style = (Style)FindResource("TransportButton"), Width = 96, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Focusable = false, Margin = new Thickness(0, 0, 8, 0) };
        choose.Click += async (_, _) => await ChoosePluginAsync();
        _pluginEdit = new Button { Content = "Open editor", Style = (Style)FindResource("TransportButton"), Width = 96, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Focusable = false, Margin = new Thickness(0, 0, 8, 0) };
        _pluginEdit.Click += (_, _) =>
        {
            var slot = _track.Model.PluginSlot;
            if (!PluginEditorWindow.Show(this, $"{slot.Info?.Name}: {_track.Name}", slot, _main.PluginStateChanged))
            {
                _main.ReportProblem("This plugin has no editor window.");
            }
        };
        _pluginClear = new Button { Content = "Remove", Style = (Style)FindResource("TransportButton"), Width = 96, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Focusable = false, Margin = new Thickness(0, 0, 12, 0) };
        _pluginClear.Click += async (_, _) =>
        {
            await _main.AssignPluginAsync(_track, null);
            Sync();
        };
        _pluginName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextDim") };
        row.Children.Add(choose);
        row.Children.Add(_pluginEdit);
        row.Children.Add(_pluginClear);
        row.Children.Add(_pluginName);
        Panels.Children.Add(row);
    }

    private async Task ChoosePluginAsync()
    {
        var picker = new PluginPickerWindow(this);
        if (picker.PickFrom(_main.ScanPluginsAsync(_track.Model.IsMidi)) && picker.Selected is { } info)
        {
            await _main.AssignPluginAsync(_track, info);
            Sync();
        }
    }

    private void Build()
    {
        BuildPluginRow();
        if (_track.Model.IsMidi && _track.Model.Machine is null)
        {
            return;
        }

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
        var plugin = _track.Model.PluginSlot;
        _pluginName.Text = plugin.Error ?? plugin.Info?.Name ?? "None";
        _pluginEdit.IsEnabled = plugin.Instance is not null;
        _pluginClear.IsEnabled = plugin.Info is not null;
        _pluginOn.IsEnabled = plugin.Info is not null;
        _pluginOn.IsChecked = plugin.Enabled;
        _syncing = true;
        var effects = _track.Effects.Effects;
        for (var e = 0; e < _toggles.Count; e++)
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
