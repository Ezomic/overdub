using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly Session _session;
    private readonly DispatcherTimer _timer;
    private string _status = "No audio device";
    private string _message = "";
    private string _notice = "";
    private string _time = "00:00.000";
    private string _bar = "Bar 1 · Beat 1";
    private bool _isRecording;
    private double _playheadX;
    private double _timelineWidth = 60 * Timeline.PixelsPerSecond;
    private double _bpm = 120;

    public MainViewModel()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Overdub",
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        _session = new Session(folder);
        _session.Tracks.Add(new Track("Guitar", 0));
        _session.Tracks.Add(new Track("Bass", 1));
        _session.Tracks.Add(new Track("Keys", null));

        Brush[] colors = [Hex("#4C9AFF"), Hex("#5BC070"), Hex("#9B8AFB")];
        Tracks = new ObservableCollection<TrackViewModel>(_session.Tracks.Select((t, i) => new TrackViewModel(t, colors[i], _session.ApplyMixerState)));

        Keys.CycleMidi = new RelayCommand(CycleMidiDevice);
        PlayCommand = new RelayCommand(TogglePlay);
        StopCommand = new RelayCommand(Stop);
        RecordCommand = new RelayCommand(ToggleRecord);

        OpenDevice();
        CycleMidiDevice();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public ObservableCollection<TrackViewModel> Tracks { get; }
    private TrackViewModel Keys => Tracks[2];
    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RecordCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value);
    }

    public string Time
    {
        get => _time;
        private set => SetField(ref _time, value);
    }

    public string Bar
    {
        get => _bar;
        private set => SetField(ref _bar, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set => SetField(ref _isRecording, value);
    }

    public bool IsPlaying => _session.Engine.IsPlaying;

    public double PlayheadX
    {
        get => _playheadX;
        private set => SetField(ref _playheadX, value);
    }

    public double TimelineWidth
    {
        get => _timelineWidth;
        private set => SetField(ref _timelineWidth, value);
    }

    public double Bpm
    {
        get => _bpm;
        set
        {
            if (SetField(ref _bpm, Math.Clamp(value, 20, 300)))
            {
                _session.Engine.Bpm = _bpm;
            }
        }
    }

    public bool Metronome
    {
        get => _session.Engine.MetronomeEnabled;
        set
        {
            _session.Engine.MetronomeEnabled = value;
            OnPropertyChanged();
        }
    }

    public string DefaultExportName => Path.Combine(_session.Directory, "Mixdown.wav");

    public void Export(string path)
    {
        try
        {
            _session.ExportMixdown(path);
            Message = "";
            Notice = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public string ProjectFolder => Path.GetDirectoryName(_session.Directory)!;

    public void Save()
    {
        try
        {
            _session.Save();
            Message = "";
            Notice = $"Saved {_session.ProjectPath}";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public void Open(string path)
    {
        if (IsRecording)
        {
            Message = "Stop recording before opening a project.";
            return;
        }

        try
        {
            _session.Engine.StopTransport();
            Bpm = _session.Load(path);
            RefreshTimeline();
            OnPropertyChanged(nameof(Metronome));
            Message = "";
            Notice = $"Opened {Path.GetFileName(Path.GetDirectoryName(path))}";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public void SeekToPixel(double x)
    {
        var engine = _session.Engine;
        if (engine.SampleRate > 0 && !IsRecording)
        {
            engine.Seek((long)(Math.Max(0, x) / Timeline.PixelsPerSecond * engine.SampleRate));
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _session.Dispose();
    }

    private void OpenDevice()
    {
        var engine = _session.Engine;
        try
        {
            var driver = AsioEngine.GetDriverNames().FirstOrDefault(n => n.Contains("Komplete", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("No Komplete Audio ASIO driver found.");
            engine.Open(driver);
            engine.Start();
            Status = $"{driver}  ·  {engine.SampleRate / 1000.0:0.#} kHz  ·  {engine.BufferSamples} samples  ·  {engine.LatencyMilliseconds:0.0} ms  ·  compensating {engine.CompensationSamples} samples";
        }
        catch (Exception ex)
        {
            Status = "No audio device";
            Message = ex.Message;
        }
    }

    private void CycleMidiDevice()
    {
        var midi = _session.Midi;
        try
        {
            var names = MidiInput.GetDeviceNames();
            var current = midi.DeviceName is null ? -1 : names.ToList().IndexOf(midi.DeviceName);
            var next = current + 1;
            if (next >= names.Count)
            {
                midi.Close();
                Keys.MidiLabel = names.Count == 0 ? "No MIDI input found" : "No MIDI input";
                return;
            }

            midi.Open(next);
            Keys.MidiLabel = names[next];
        }
        catch (Exception ex)
        {
            midi.Close();
            Keys.MidiLabel = "No MIDI input";
            Message = $"MIDI: {ex.Message}";
        }
    }

    private void TogglePlay()
    {
        var engine = _session.Engine;
        if (engine.SampleRate == 0 || IsRecording)
        {
            return;
        }

        if (engine.IsPlaying)
        {
            engine.Pause();
        }
        else
        {
            engine.Play();
        }

        OnPropertyChanged(nameof(IsPlaying));
    }

    private void Stop()
    {
        if (IsRecording)
        {
            FinishRecording();
        }

        _session.Engine.StopTransport();
        OnPropertyChanged(nameof(IsPlaying));
    }

    private void ToggleRecord()
    {
        var engine = _session.Engine;
        if (engine.SampleRate == 0)
        {
            Message = "Connect your Komplete Audio and restart Overdub.";
            return;
        }

        if (IsRecording)
        {
            FinishRecording();
            engine.Pause();
            OnPropertyChanged(nameof(IsPlaying));
            return;
        }

        if (!_session.CanRecord)
        {
            Message = "Arm a track first.";
            return;
        }

        if (_session.HasArmedMidi && !_session.Midi.IsOpen)
        {
            Message = "Pick a MIDI input on the Keys track first.";
            return;
        }

        Message = "";
        _session.StartRecording();
        IsRecording = true;
        OnPropertyChanged(nameof(IsPlaying));
    }

    private void FinishRecording()
    {
        _session.StopRecording();
        IsRecording = false;
        RefreshTimeline();
        _session.Save();
    }

    private void RefreshTimeline()
    {
        foreach (var track in Tracks)
        {
            track.RefreshClips(_session.Engine.SampleRate);
            track.NotifyMixChanged();
        }

        var seconds = (double)_session.LengthSamples / Math.Max(1, _session.Engine.SampleRate);
        TimelineWidth = Math.Max(60, seconds + 10) * Timeline.PixelsPerSecond;
    }

    private void Tick()
    {
        var engine = _session.Engine;
        if (engine.SampleRate == 0)
        {
            return;
        }

        var time = engine.PositionTime;
        Time = $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
        var samplesPerBeat = engine.SampleRate * 60.0 / engine.Bpm;
        var beat = (long)(engine.Position / samplesPerBeat);
        Bar = $"Bar {(beat / 4) + 1} · Beat {(beat % 4) + 1}";
        PlayheadX = time.TotalSeconds * Timeline.PixelsPerSecond;
        foreach (var track in Tracks)
        {
            track.UpdateMeter(engine);
        }

        Keys.UpdateMidiMeter(_session.ReadMidiActivity());
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
