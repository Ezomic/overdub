using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public partial class TunerWindow : Window
{
    private const int WindowSize = 4096;

    private readonly AsioEngine _engine;
    private readonly DispatcherTimer _timer;
    private readonly float[] _window = new float[WindowSize];
    private readonly Queue<double> _recent = new();
    private int _lastHeard = Environment.TickCount;
    private bool _busy;

    public TunerWindow(MainViewModel viewModel)
    {
        _engine = viewModel.Engine;
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
            chip.Click += (_, _) => Select(index);
            InputChips.Children.Add(chip);
        }

        Select(0);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _timer.Tick += async (_, _) => await Update();
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _engine.TunerInput = -1;
        };
    }

    private void Select(int index)
    {
        _engine.TunerInput = _engine.InputCount > index ? index : -1;
        _recent.Clear();
        for (var i = 0; i < InputChips.Children.Count; i++)
        {
            ((ToggleButton)InputChips.Children[i]).IsChecked = i == index;
        }
    }

    private async Task Update()
    {
        if (_busy || _engine.SampleRate == 0 || _engine.TunerInput < 0)
        {
            return;
        }

        _busy = true;
        try
        {
            _engine.CopyTunerWindow(_window);
            var rate = _engine.SampleRate;
            var snapshot = (float[])_window.Clone();
            var result = await Task.Run(() => PitchDetector.Detect(snapshot, rate));
            if (result is { Clarity: > 0.8 } found)
            {
                _lastHeard = Environment.TickCount;
                _recent.Enqueue(found.Frequency);
                while (_recent.Count > 5)
                {
                    _recent.Dequeue();
                }

                var median = _recent.OrderBy(f => f).ElementAt(_recent.Count / 2);
                Show(PitchDetector.ToNote(median));
            }
            else if (Environment.TickCount - _lastHeard > 600)
            {
                _recent.Clear();
                NoteText.Text = "--";
                OctaveText.Text = "";
                FrequencyText.Text = "Play a single note";
                CentsText.Text = "";
                Canvas.SetLeft(Needle, 148);
                Needle.Fill = (Brush)FindResource("TextDim");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(NoteReading note)
    {
        NoteText.Text = note.Name;
        OctaveText.Text = note.Octave.ToString();
        FrequencyText.Text = $"{note.Frequency:0.0} Hz";
        var cents = Math.Clamp(note.Cents, -50, 50);
        Canvas.SetLeft(Needle, 148 + (cents * 3));
        var inTune = Math.Abs(note.Cents) <= 5;
        Needle.Fill = (Brush)FindResource(inTune ? "Good" : Math.Abs(note.Cents) <= 15 ? "TextDim" : "Danger");
        CentsText.Text = inTune ? "In tune" : note.Cents < 0 ? $"{-note.Cents:0} cents flat, tune up" : $"{note.Cents:0} cents sharp, tune down";
    }
}
