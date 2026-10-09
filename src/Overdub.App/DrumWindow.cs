using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class DrumWindow : Window
{
    private const double StepWidth = 26;
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));

    private readonly MainViewModel _main;
    private readonly StackPanel _patternBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _grid = new();
    private readonly TextBlock _hint = new() { Margin = new Thickness(0, 12, 0, 0) };
    private DrumPattern? _current;
    private int _repeat = 1;
    private bool _painting;
    private byte _paintLevel;

    public DrumWindow(MainViewModel main, Window owner)
    {
        _main = main;
        Owner = owner;
        Icon = owner.Icon;
        Title = "Drum machine";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _hint.Foreground = (Brush)FindResource("TextDim");
        _hint.Text = "Click a step to cycle off, hit, accent. Right-click a step to clear it or set its velocity and how often it plays (the bar height shows velocity, a faded step plays only some of the time). Place puts the pattern at the playhead as a block you can move, copy and delete on the timeline.";
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.MaxWidth = 640;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(_patternBar);
        panel.Children.Add(_grid);
        panel.Children.Add(_hint);
        Content = panel;
        _main.EditHistoryChanged += OnHistory;
        Closed += (_, _) => _main.EditHistoryChanged -= OnHistory;
        Refresh();
    }

    private Track? Track => _main.DrumTrack;

    private void OnHistory()
    {
        if (Mouse.Captured is not Slider && !_painting)
        {
            Refresh();
        }
    }

    private Button MakeButton(string text, Action click, bool selected = false, double width = double.NaN)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("TransportButton"),
            Width = width,
            Height = 26,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Focusable = false,
        };
        if (selected)
        {
            button.BorderBrush = Accent;
            button.Foreground = Accent;
        }

        button.Click += (_, _) => click();
        return button;
    }

    private void Refresh()
    {
        if (Track is not { } track)
        {
            Close();
            return;
        }

        _current = track.Patterns.FirstOrDefault(p => p.Id == _current?.Id) ?? track.Patterns.FirstOrDefault();
        _patternBar.Children.Clear();
        foreach (var pattern in track.Patterns)
        {
            var target = pattern;
            _patternBar.Children.Add(MakeButton(pattern.Name, () =>
            {
                _current = target;
                Refresh();
            }, pattern == _current, 36));
        }

        if (track.Patterns.Count < 8)
        {
            _patternBar.Children.Add(MakeButton("+", () =>
            {
                _current = _main.AddDrumPattern();
                Refresh();
            }, width: 30));
        }

        if (_current is { } current)
        {
            _patternBar.Children.Add(new Border { Width = 16 });
            _patternBar.Children.Add(MakeButton(current.Bars == 1 ? "1 bar" : "2 bars", () => _main.SetDrumBars(current, current.Bars == 1 ? 2 : 1), width: 70));
            var presets = MakeButton("Presets", () => { }, width: 80);
            presets.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = presets, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                foreach (var (name, _) in DrumPattern.Presets)
                {
                    var preset = name;
                    var item = new MenuItem { Header = preset };
                    item.Click += (_, _) => _main.ApplyDrumPreset(current, preset);
                    menu.Items.Add(item);
                }

                menu.IsOpen = true;
            };
            _patternBar.Children.Add(presets);
            _patternBar.Children.Add(MakeButton($"Swing: {DrumPattern.SwingNames[current.Swing].ToLowerInvariant()}", () => _main.SetDrumSwing(current), width: 120));
            _patternBar.Children.Add(MakeButton($"Feel: {Humanizer.FeelNames[current.Feel]}", () => _main.SetDrumFeel(current), width: 100));
            _patternBar.Children.Add(MakeButton("Clear", () => _main.ClearDrumPattern(current), width: 60));
            _patternBar.Children.Add(MakeButton($"Repeat {_repeat}x", () =>
            {
                _repeat = _repeat >= 8 ? 1 : _repeat * 2;
                Refresh();
            }, width: 80));
            _patternBar.Children.Add(MakeButton("Place at playhead", () => _main.PlaceDrumPattern(current, _repeat), width: 130));
        }

        BuildGrid();
    }

    private void BuildGrid()
    {
        _grid.Children.Clear();
        if (_current is not { } pattern)
        {
            return;
        }

        for (var lane = 0; lane < DrumKit.Lanes.Count; lane++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(LaneControls(lane));
            for (var step = 0; step < pattern.Steps; step++)
            {
                row.Children.Add(StepCell(pattern, lane, step));
            }

            _grid.Children.Add(row);
        }
    }

    private StackPanel LaneControls(int lane)
    {
        var mix = _main.DrumLane(lane) ?? new DrumLaneMix();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 232 };
        var mute = new System.Windows.Controls.Primitives.ToggleButton { Content = "M", Style = (Style)FindResource("Chip"), IsChecked = mix.Mute, Focusable = false, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Mute this sound" };
        mute.Click += (_, _) => _main.SetDrumLane(lane, null, null, mute.IsChecked == true);
        var volume = new Slider { Minimum = 0, Maximum = 1.5, Value = mix.Gain, Width = 52, Focusable = false, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume (double-click for 100%)" };
        volume.ValueChanged += (_, e) => _main.SetDrumLane(lane, (float)e.NewValue, null, null);
        volume.MouseDoubleClick += (_, _) => volume.Value = 1;
        var pan = new Slider { Minimum = -1, Maximum = 1, Value = mix.Pan, Width = 40, Focusable = false, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Pan (double-click for center)" };
        pan.ValueChanged += (_, e) => _main.SetDrumLane(lane, null, (float)e.NewValue, null);
        pan.MouseDoubleClick += (_, _) => pan.Value = 0;
        panel.Children.Add(mute);
        panel.Children.Add(volume);
        panel.Children.Add(pan);
        panel.Children.Add(new TextBlock { Text = DrumKit.Lanes[lane].Name, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextDim") });
        return panel;
    }

    private Border StepCell(DrumPattern pattern, int lane, int step)
    {
        var cell = new Border
        {
            Width = StepWidth,
            Height = 28,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(step % 4 == 0 && step > 0 ? 8 : 2, 0, 2, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Border"),
            Cursor = Cursors.Hand,
        };
        Look(cell, pattern, lane, step);
        cell.MouseLeftButtonDown += (_, _) =>
        {
            _paintLevel = (byte)((pattern.Get(lane, step) + 1) % 3);
            _painting = true;
            Paint(cell, pattern, lane, step);
        };
        cell.MouseEnter += (_, _) =>
        {
            if (_painting && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                Paint(cell, pattern, lane, step);
            }
        };
        cell.MouseRightButtonDown += (_, _) => StepMenu(cell, pattern, lane, step, pattern.Get(lane, step));
        return cell;
    }

    private void Paint(Border cell, DrumPattern pattern, int lane, int step)
    {
        if (pattern.Get(lane, step) == _paintLevel)
        {
            return;
        }

        _main.SetDrumStep(pattern, lane, step, _paintLevel, painting: true);
        Look(cell, pattern, lane, step);
        if (_paintLevel > 0)
        {
            _main.AuditionDrum(lane, _paintLevel == 2 ? 127 : 90);
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_painting)
        {
            _painting = false;
            Dispatcher.BeginInvoke(Refresh);
        }
    }

    private void Look(Border cell, DrumPattern pattern, int lane, int step)
    {
        var level = pattern.Get(lane, step);
        cell.Background = (Brush)FindResource(step / 4 % 2 == 0 ? "Panel" : "Lane");
        cell.Child = null;
        cell.ToolTip = null;
        if (level == 0)
        {
            return;
        }

        var chance = pattern.Chance(lane, step);
        cell.Child = new Border
        {
            Background = level == 2 ? Accent : (Brush)FindResource("Good"),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = Math.Max(4, 26 * pattern.Velocity(lane, step) / 127.0),
            Opacity = 0.4 + (0.6 * chance / 100),
        };
        cell.ToolTip = $"Velocity {pattern.Velocity(lane, step)}, plays {chance}% of the time. Right-click to change.";
    }

    private void StepMenu(FrameworkElement target, DrumPattern pattern, int lane, int step, byte level)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        var clear = new MenuItem { Header = "Clear step" };
        clear.Click += (_, _) => _main.SetDrumStep(pattern, lane, step, 0);
        menu.Items.Add(clear);
        if (level > 0)
        {
            var velocity = new MenuItem { Header = "Velocity" };
            foreach (var v in new[] { 30, 50, 70, 90, 110, 127 })
            {
                var value = v;
                var item = new MenuItem { Header = value.ToString(), IsChecked = pattern.Velocity(lane, step) == value };
                item.Click += (_, _) => _main.SetDrumStepDetail(pattern, lane, step, value, null);
                velocity.Items.Add(item);
            }

            var chance = new MenuItem { Header = "Chance to play" };
            foreach (var c in new[] { 100, 75, 50, 25 })
            {
                var value = c;
                var item = new MenuItem { Header = $"{value}%", IsChecked = pattern.Chance(lane, step) == value };
                item.Click += (_, _) => _main.SetDrumStepDetail(pattern, lane, step, null, value);
                chance.Items.Add(item);
            }

            menu.Items.Add(velocity);
            menu.Items.Add(chance);
        }

        menu.IsOpen = true;
    }
}
