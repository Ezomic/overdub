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
    private string _midiLabel = "No MIDI input";

    private static readonly Brush[] Palette =
    [
        Hex("#4C9AFF"), Hex("#5BC070"), Hex("#9B8AFB"), Hex("#E5A33B"), Hex("#E5619B"), Hex("#2EC4B6"),
    ];

    public MainViewModel()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Overdub",
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        _session = new Session(folder);
        _session.AddTrack("Guitar", 0);
        _session.AddTrack("Bass", 1);
        _session.AddTrack("Keys", null);
        Tracks = [];
        RebuildTracks();

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

    public string CountInLabel => _session.Engine.CountInBars switch
    {
        0 => "Count-in: off",
        1 => "Count-in: 1 bar",
        _ => $"Count-in: {_session.Engine.CountInBars} bars",
    };

    public void CycleCountIn()
    {
        _session.Engine.CountInBars = (_session.Engine.CountInBars + 1) % 3;
        OnPropertyChanged(nameof(CountInLabel));
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
            RebuildTracks();
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

    public int InputCount => Math.Max(2, _session.Engine.InputCount);

    public void AddAudioTrack(int input)
    {
        _session.AddTrack(UniqueName($"Input {input + 1}"), input);
        RebuildTracks();
    }

    public void AddMidiTrack()
    {
        _session.AddTrack(UniqueName("Keys"), null);
        RebuildTracks();
    }

    private void RemoveTrack(TrackViewModel track)
    {
        if (IsRecording)
        {
            Message = "Stop recording before removing a track.";
            return;
        }

        _session.RemoveTrack(track.Model);
        Tracks.Remove(track);
        RefreshTimeline();
        _session.Save();
    }

    private void DisarmConflicts(TrackViewModel armed)
    {
        foreach (var other in Tracks)
        {
            if (other != armed && other.Armed && other.Model.Input == armed.Model.Input)
            {
                other.Armed = false;
            }
        }
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var n = 2; Tracks.Any(t => t.Name == candidate); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }

    private void RebuildTracks()
    {
        Tracks.Clear();
        foreach (var model in _session.Tracks)
        {
            var vm = new TrackViewModel(model, Palette[model.ColorIndex % Palette.Length], _session.ApplyMixerState, DisarmConflicts, RemoveTrack)
            {
                MidiLabel = _midiLabel,
            };
            if (model.IsMidi)
            {
                vm.CycleMidi = new RelayCommand(CycleMidiDevice);
            }

            Tracks.Add(vm);
        }

        if (_session.Engine.SampleRate > 0)
        {
            RefreshTimeline();
        }
    }

    private void SetMidiLabel(string label)
    {
        _midiLabel = label;
        foreach (var track in Tracks)
        {
            track.MidiLabel = label;
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
                SetMidiLabel(names.Count == 0 ? "No MIDI input found" : "No MIDI input");
                return;
            }

            midi.Open(next);
            SetMidiLabel(names[next]);
        }
        catch (Exception ex)
        {
            midi.Close();
            SetMidiLabel("No MIDI input");
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
            Message = "Pick a MIDI input on a Keys track first.";
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

        var activity = _session.ReadMidiActivity();
        foreach (var track in Tracks)
        {
            if (track.IsMidi)
            {
                track.UpdateMidiMeter(activity);
            }
        }
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
