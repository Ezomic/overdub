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

    private static readonly (int Beats, int Unit)[] Signatures = [(4, 4), (3, 4), (2, 4), (5, 4), (6, 8), (7, 8), (9, 8), (12, 8)];

    private readonly TapTempo _tapTempo = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private static readonly Brush LoopBrush = Hex("#334C9AFF");
    private static readonly Brush PunchBrush = Hex("#33E5484D");
    private long _regionStart;
    private long _regionEnd;
    private bool _loopOn;
    private bool _punchOn;

    public bool HasRegion => _regionEnd > _regionStart;
    public double RegionX => SamplesToPixels(_regionStart);
    public double RegionWidth => Math.Max(0, SamplesToPixels(_regionEnd) - RegionX);
    public Brush RegionBrush => _punchOn ? PunchBrush : LoopBrush;

    public string RegionText
    {
        get
        {
            var engine = _session.Engine;
            if (!HasRegion || engine.SampleRate == 0)
            {
                return "No region. Drag on the ruler to set one, double-click it to clear.";
            }

            var bars = (_regionEnd - _regionStart) / (engine.SamplesPerBeat * engine.BeatsPerBar);
            return $"Region {Clock(_regionStart)} to {Clock(_regionEnd)}, {bars:0.##} bars";
        }
    }

    public bool LoopOn
    {
        get => _loopOn;
        set
        {
            _loopOn = value;
            if (value)
            {
                _punchOn = false;
            }

            RegionChanged();
        }
    }

    public bool PunchOn
    {
        get => _punchOn;
        set
        {
            _punchOn = value;
            if (value)
            {
                _loopOn = false;
            }

            RegionChanged();
        }
    }

    public void SetRegion(double fromPixel, double toPixel)
    {
        var engine = _session.Engine;
        if (engine.SampleRate == 0)
        {
            return;
        }

        var start = SnapToBeat(PixelsToSamples(Math.Min(fromPixel, toPixel)));
        var end = SnapToBeat(PixelsToSamples(Math.Max(fromPixel, toPixel)));
        if (end - start < engine.SamplesPerBeat * 0.5)
        {
            ClearRegion();
            return;
        }

        _regionStart = start;
        _regionEnd = end;
        RegionChanged();
    }

    public void ClearRegion()
    {
        _regionStart = 0;
        _regionEnd = 0;
        RegionChanged();
    }

    private void RegionChanged()
    {
        var engine = _session.Engine;
        engine.LoopEnabled = _loopOn && HasRegion;
        engine.LoopStart = _regionStart;
        engine.LoopEnd = _regionEnd;
        engine.PunchEnabled = _punchOn && HasRegion;
        engine.PunchIn = _regionStart;
        engine.PunchOut = _regionEnd;
        foreach (var name in new[] { nameof(LoopOn), nameof(PunchOn), nameof(HasRegion), nameof(RegionX), nameof(RegionWidth), nameof(RegionBrush), nameof(RegionText) })
        {
            OnPropertyChanged(name);
        }
    }

    private double SamplesToPixels(long samples) =>
        _session.Engine.SampleRate == 0 ? 0 : (double)samples / _session.Engine.SampleRate * Timeline.PixelsPerSecond;

    private long PixelsToSamples(double pixels) => (long)(Math.Max(0, pixels) / Timeline.PixelsPerSecond * _session.Engine.SampleRate);

    private long SnapToBeat(long samples)
    {
        var beat = _session.Engine.SamplesPerBeat;
        return (long)(Math.Round(samples / beat) * beat);
    }

    private static string Clock(long samples, int rate = 44100) => $"{(int)(samples / rate / 60)}:{samples / (double)rate % 60:00.0}";

    public string SignatureLabel => $"{_session.Engine.BeatsPerBar}/{_session.Engine.BeatUnit}";

    public void CycleSignature()
    {
        var engine = _session.Engine;
        var index = Array.IndexOf(Signatures, (engine.BeatsPerBar, engine.BeatUnit));
        (engine.BeatsPerBar, engine.BeatUnit) = Signatures[(index + 1) % Signatures.Length];
        OnPropertyChanged(nameof(SignatureLabel));
    }

    public void Tap()
    {
        if (_tapTempo.Tap(_clock.ElapsedMilliseconds) is { } bpm)
        {
            Bpm = Math.Round(bpm);
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
            OnPropertyChanged(nameof(SignatureLabel));
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

    private string _driver = "";

    private void UpdateStatus()
    {
        var engine = _session.Engine;
        var kind = engine.MeasuredRoundTripSamples is null ? "estimated" : "measured";
        Status = $"{_driver}  ·  {engine.SampleRate / 1000.0:0.#} kHz  ·  {engine.BufferSamples} samples  ·  {engine.LatencyMilliseconds:0.0} ms  ·  latency {kind} {engine.CompensationSamples} samples";
    }

    public AsioEngine Engine => _session.Engine;

    public int AudioInputCount => Math.Max(1, _session.Engine.InputCount);

    public string LatencySummary
    {
        get
        {
            var engine = _session.Engine;
            var estimate = engine.EstimatedRoundTripSamples;
            return engine.MeasuredRoundTripSamples is { } measured
                ? $"Measured {measured} samples ({measured * 1000.0 / engine.SampleRate:0.0} ms). The estimate was {estimate}."
                : $"Using the estimate: {estimate} samples ({estimate * 1000.0 / Math.Max(1, engine.SampleRate):0.0} ms).";
        }
    }

    public async Task<string> MeasureLatencyAsync(int input)
    {
        var engine = _session.Engine;
        if (engine.SampleRate == 0)
        {
            return "No audio device.";
        }

        try
        {
            var result = await engine.MeasureLatencyAsync(input);
            UpdateStatus();
            return result is null
                ? $"No signal heard on input {input + 1}. Check the cable from an output to that input and raise the input gain."
                : LatencySummary;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void ResetLatency()
    {
        _session.Engine.ClearMeasuredLatency();
        UpdateStatus();
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
            _driver = driver;
            UpdateStatus();
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

        var engineNow = _session.Engine;
        if (_punchOn)
        {
            if (!HasRegion)
            {
                Message = "Drag on the ruler to set the punch range first.";
                return;
            }

            engineNow.Seek(Math.Max(0, _regionStart - (long)(engineNow.SamplesPerBeat * engineNow.BeatsPerBar)));
        }
        else if (_loopOn && HasRegion)
        {
            engineNow.Seek(_regionStart);
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
        var beat = (long)(engine.Position / engine.SamplesPerBeat);
        Bar = $"Bar {(beat / engine.BeatsPerBar) + 1} · Beat {(beat % engine.BeatsPerBar) + 1}";
        PlayheadX = time.TotalSeconds * Timeline.PixelsPerSecond;
        if (IsRecording && engine.PunchCompleted)
        {
            FinishRecording();
            Notice = "Punch recorded";
        }

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
