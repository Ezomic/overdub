using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public sealed class BassPracticeWindow : Window
{
    private readonly MainViewModel _main;
    private readonly Track _track;
    private readonly FretboardControl _fretboard = new() { Height = 190, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _chord = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _tones = new() { Margin = new Thickness(14, 8, 0, 0) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private string _last = "";
    private readonly TabScrollControl _tab = new() { Height = 170, Margin = new Thickness(0, 8, 0, 6) };
    private readonly System.Windows.Controls.Primitives.ToggleButton _mute = new() { Content = "Mute the bass so I play it", Style = null!, Focusable = false };

    public BassPracticeWindow(MainViewModel main, Window owner, Track track)
    {
        _main = main;
        _track = track;
        Owner = owner;
        Icon = owner.Icon;
        Title = $"Bass practice: {track.Name}";
        Width = 960;
        SizeToContent = SizeToContent.Height;
        MinWidth = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _tones.Foreground = (Brush)FindResource("TextDim");
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(_chord);
        head.Children.Add(_tones);
        var legend = new TextBlock
        {
            Text = "R root   3 third   5 fifth   7 seventh. Dim dots are the other notes of the key. The ring shows the note the bass machine is playing right now. Follows the playhead, so press play and watch where the chord lives on the neck.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var scale = new System.Windows.Controls.Primitives.ToggleButton { Content = "Show the key's scale", Style = (Style)FindResource("Chip"), IsChecked = true, Focusable = false, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        scale.Click += (_, _) =>
        {
            _fretboard.ShowScale = scale.IsChecked == true;
            _fretboard.InvalidateVisual();
        };
        _mute.Style = (Style)FindResource("Chip");
        _mute.Margin = new Thickness(0, 0, 8, 0);
        _mute.HorizontalAlignment = HorizontalAlignment.Left;
        _mute.IsChecked = track.Mute;
        _mute.Click += (_, _) => _main.SetMachineMute(_track, _mute.IsChecked == true);
        var tabTitle = new TextBlock { Text = "Play along", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) };
        var tabHint = new TextBlock
        {
            Text = "The bass line scrolls past the white line as tab. Green notes are coming, amber is now, grey is done. Mute the bass machine, play the line yourself, then unmute to hear it against yours. Use Loop and Speed (or the tempo ramp) in the main window to work on a hard part.",
            Foreground = (Brush)FindResource("TextDim"),
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(head);
        panel.Children.Add(_fretboard);
        panel.Children.Add(legend);
        panel.Children.Add(scale);
        panel.Children.Add(tabTitle);
        panel.Children.Add(tabHint);
        panel.Children.Add(_tab);
        panel.Children.Add(_mute);
        var check = new Button { Content = "Check my playing", Style = (Style)FindResource("TransportButton"), Width = 150, Height = 26, FontSize = 12, Margin = new Thickness(0, 12, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Focusable = false, ToolTip = "Compare the selected audio clip (your recorded bass take) with the bass machine line" };
        var result = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, BorderThickness = new Thickness(0), Background = (Brush)FindResource("Well"), Foreground = (Brush)FindResource("Text"), Padding = new Thickness(10), Text = "Record a take of yourself playing the line, click the clip in the main window, then check it. I compare every note with the bass machine: right note, early or late, wrong octave, missed." };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            result.Text = "Listening...";
            result.Text = await _main.CheckPlayingAsync(_track);
            check.IsEnabled = true;
        };
        panel.Children.Add(check);
        panel.Children.Add(result);
        Content = panel;
        _main.EditHistoryChanged += LoadTab;
        Closed += (_, _) => _main.EditHistoryChanged -= LoadTab;
        LoadTab();
        _clock.Tick += (_, _) => Update();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        Update();
    }

    private void LoadTab()
    {
        if (!_main.HasTrack(_track))
        {
            return;
        }

        (_tab.Notes, _tab.Bars) = _main.BassTabData(_track);
        _mute.IsChecked = _track.Mute;
    }

    private void Update()
    {
        if (!_main.HasTrack(_track))
        {
            Close();
            return;
        }

        _tab.Position = _main.Engine.Position;
        _tab.SampleRate = Math.Max(1, _main.Engine.SampleRate);
        _tab.Playing = _main.Engine.IsPlaying;
        _tab.InvalidateVisual();
        var chord = _main.ChordAtPlayhead(_track);
        var playing = _main.BassPitchAtPlayhead(_track);
        var key = _main.DetectedKey();
        var signature = $"{chord?.Root}:{chord?.Quality}:{playing}:{key?.Root}:{key?.Scale}";
        if (signature == _last)
        {
            return;
        }

        _last = signature;
        _fretboard.Chord = chord;
        _fretboard.Key = key;
        _fretboard.PlayingPitch = playing;
        _chord.Text = chord is { } c ? c.Name : "No chord yet";
        _tones.Text = chord is { } c2 ? string.Join("  ", c2.Intervals.Select(i => Chord.Roots[(c2.Root + i) % 12])) : "Add a chord pattern to this bass track";
        _fretboard.InvalidateVisual();
    }
}
