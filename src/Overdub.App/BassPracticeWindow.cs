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
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private string _last = "";

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
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(head);
        panel.Children.Add(_fretboard);
        panel.Children.Add(legend);
        panel.Children.Add(scale);
        Content = panel;
        _clock.Tick += (_, _) => Update();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        Update();
    }

    private void Update()
    {
        if (!_main.HasTrack(_track))
        {
            Close();
            return;
        }

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
