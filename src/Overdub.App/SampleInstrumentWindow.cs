using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class SampleInstrumentWindow : Window
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private readonly MainViewModel _main;
    private readonly TrackViewModel _track;
    private readonly IReadOnlyList<(string Label, Clip Clip)> _takes;
    private readonly Button _clipButton;
    private readonly Button _roleButton;
    private readonly TextBox _name = new() { Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _keys = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
    private readonly TextBlock _keyLabels = new() { FontSize = 11 };
    private readonly Button _create;
    private Clip? _clip;
    private MachineRole _role;
    private SamplingAnalysis? _analysis;

    public string? CreatedPreset { get; private set; }

    public SampleInstrumentWindow(MainViewModel main, Window owner, TrackViewModel track)
    {
        _main = main;
        _track = track;
        _role = track.Model.Machine == MachineRole.Bass ? MachineRole.Bass : MachineRole.Guitar;
        _takes = main.AudioTakes;
        _clip = _takes.FirstOrDefault().Clip;
        Owner = owner;
        Icon = owner.Icon;
        Title = "Sample my instrument";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Record a slow chromatic run on an audio track first: one note at a time, low to high, let each ring for about a second. Then pick that recording here and Overdub turns it into a sampled instrument.", Foreground = (Brush)FindResource("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

        _clipButton = Chooser(_clip is null ? "No audio recordings yet" : _takes[0].Label, 400);
        _clipButton.Click += (_, _) => ShowMenu(_clipButton, _takes.Select(t => (t.Label, (Action)(() => { _clip = t.Clip; _clipButton.Content = t.Label; ResetAnalysis(); }))));
        panel.Children.Add(Label("Recording"));
        panel.Children.Add(_clipButton);

        _roleButton = Chooser(RoleName(_role), 160);
        _roleButton.Click += (_, _) => ShowMenu(_roleButton, new[] { MachineRole.Bass, MachineRole.Guitar }.Select(r => (RoleName(r), (Action)(() => { _role = r; _roleButton.Content = RoleName(r); ResetAnalysis(); }))));
        panel.Children.Add(Label("Instrument"));
        panel.Children.Add(_roleButton);

        _name.Text = _role == MachineRole.Bass ? "My bass" : "My guitar";
        panel.Children.Add(Label("Name"));
        panel.Children.Add(_name);

        var analyseRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var analyse = Chooser("Analyse", 100);
        analyse.Click += async (_, _) => await AnalyseAsync();
        DockPanel.SetDock(analyse, Dock.Right);
        analyseRow.Children.Add(analyse);
        _result.Foreground = (Brush)FindResource("TextDim");
        _result.Text = "Click Analyse to see which notes the recording holds.";
        analyseRow.Children.Add(_result);
        panel.Children.Add(analyseRow);
        panel.Children.Add(_keys);
        _keyLabels.Foreground = (Brush)FindResource("TextDim");
        panel.Children.Add(_keyLabels);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Chooser("Cancel", 80);
        cancel.IsCancel = true;
        _create = Chooser("Create instrument", 140);
        _create.IsDefault = true;
        _create.IsEnabled = _clip is not null;
        _create.Click += async (_, _) => await CreateAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_create);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static string RoleName(MachineRole role) => role == MachineRole.Bass ? "Bass" : "Guitar";

    private static string NoteName(int pitch) => $"{NoteNames[pitch % 12]}{(pitch / 12) - 1}";

    private TextBlock Label(string text) => new() { Text = text, Foreground = (Brush)FindResource("TextDim"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) };

    private Button Chooser(string text, double width) => new()
    {
        Content = text,
        Style = (Style)FindResource("TransportButton"),
        MinWidth = width,
        Height = 28,
        FontSize = 12,
        Padding = new Thickness(12, 0, 12, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Focusable = false,
        Margin = new Thickness(0, 0, 8, 0),
    };

    private static void ShowMenu(Button anchor, IEnumerable<(string Label, Action Pick)> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (label, pick) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => pick();
            menu.Items.Add(item);
        }

        menu.IsOpen = menu.Items.Count > 0;
    }

    private void ResetAnalysis()
    {
        _analysis = null;
        _keys.Children.Clear();
        _keyLabels.Text = "";
        _result.Text = "Click Analyse to see which notes the recording holds.";
        _create.IsEnabled = _clip is not null;
    }

    private async Task<bool> AnalyseAsync()
    {
        if (_clip is null)
        {
            return false;
        }

        if (_main.Engine.SampleRate == 0)
        {
            _result.Text = "Connect your Komplete Audio first, so the recording's sample rate is known.";
            return false;
        }

        _result.Text = "Listening...";
        var clip = _clip;
        var role = _role;
        var analysis = await Task.Run(() => _main.AnalyseRecording(clip, role));
        if (clip != _clip || role != _role)
        {
            return false;
        }

        _analysis = analysis;
        _keys.Children.Clear();
        if (analysis.Slices.Count == 0)
        {
            _result.Text = "No clear single notes found. Play one note at a time and let each ring; check the recording is the instrument input.";
            _keyLabels.Text = "";
            _create.IsEnabled = false;
            return false;
        }

        var captured = analysis.Captured.ToHashSet();
        var span = analysis.HighKey - analysis.LowKey + 1;
        var width = Math.Max(4, Math.Min(18, 500.0 / span));
        for (var p = analysis.LowKey; p <= analysis.HighKey; p++)
        {
            _keys.Children.Add(new Border { Width = width - 2, Height = 22, Margin = new Thickness(0, 0, 2, 0), CornerRadius = new CornerRadius(2), Background = captured.Contains(p) ? new SolidColorBrush(Color.FromRgb(0x4F, 0x8A, 0x6A)) : (Brush)FindResource("Panel"), ToolTip = NoteName(p) });
        }

        _keyLabels.Text = $"{NoteName(analysis.LowKey)} to {NoteName(analysis.HighKey)}";
        var gaps = analysis.Gaps.Count == 0 ? "no gaps" : $"{analysis.Gaps.Count} {(analysis.Gaps.Count == 1 ? "gap" : "gaps")} ({string.Join(", ", analysis.Gaps.Take(6).Select(NoteName))}{(analysis.Gaps.Count > 6 ? ", ..." : "")}) that will borrow the nearest sample";
        _result.Text = $"Found {analysis.Captured.Count} notes, {NoteName(analysis.LowKey)} to {NoteName(analysis.HighKey)}, {gaps}.";
        _create.IsEnabled = true;
        return true;
    }

    private async Task CreateAsync()
    {
        if (_analysis is null && !await AnalyseAsync())
        {
            return;
        }

        var clip = _clip!;
        var analysis = _analysis!;
        var name = _name.Text.Trim().Length == 0 ? (_role == MachineRole.Bass ? "My bass" : "My guitar") : _name.Text.Trim();
        _create.IsEnabled = false;
        _result.Text = "Writing samples...";
        try
        {
            CreatedPreset = await Task.Run(() => _main.CreateInstrument(clip, analysis, name));
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _result.Text = ex.Message;
            _create.IsEnabled = true;
            return;
        }

        if (_track.Model.Machine == _role)
        {
            _track.SetPreset(CreatedPreset);
        }

        _main.ShowNotice($"Made {name} from {analysis.Captured.Count} sampled notes. It is in the sound picker under Other.");
        Close();
    }
}
