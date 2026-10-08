using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows;
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
        _session.History.Changed += OnHistoryChanged;
        _session.NoteActivity += OnNoteForChord;

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
                GridChanged();
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
                return "No region. Drag on the ruler to set one.";
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

        var start = SnapSamples(PixelsToSamples(Math.Min(fromPixel, toPixel)));
        var end = SnapSamples(PixelsToSamples(Math.Max(fromPixel, toPixel)));
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

    private enum SnapMode
    {
        Beat,
        Bar,
        Off,
    }

    private SnapMode _snap = SnapMode.Beat;

    public string SnapLabel => _snap switch
    {
        SnapMode.Beat => "Snap: beat",
        SnapMode.Bar => "Snap: bar",
        _ => "Snap: off",
    };

    public void CycleSnap()
    {
        _snap = (SnapMode)(((int)_snap + 1) % 3);
        OnPropertyChanged(nameof(SnapLabel));
    }

    public long SnapSamples(long samples)
    {
        var engine = _session.Engine;
        var step = _snap switch
        {
            SnapMode.Beat => engine.SamplesPerBeat,
            SnapMode.Bar => engine.SamplesPerBeat * engine.BeatsPerBar,
            _ => 0,
        };
        return step <= 0 ? samples : (long)(Math.Round(samples / step) * step);
    }

    public GridInfo Grid => new(
        _session.Engine.SampleRate == 0 ? 0 : _session.Engine.SamplesPerBeat / _session.Engine.SampleRate * Timeline.PixelsPerSecond,
        _session.Engine.BeatsPerBar);

    public Brush GridBrush
    {
        get
        {
            var grid = Grid;
            if (grid.PixelsPerBeat < 1)
            {
                return Brushes.Transparent;
            }

            var barWidth = grid.PixelsPerBeat * grid.BeatsPerBar;
            var group = new DrawingGroup();
            for (var beat = 0; beat < grid.BeatsPerBar; beat++)
            {
                var color = beat == 0 ? Color.FromRgb(0x44, 0x44, 0x4B) : Color.FromRgb(0x2A, 0x2A, 0x2F);
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(beat * grid.PixelsPerBeat, 0, 1, 124))));
            }

            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, barWidth, 124))));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, barWidth, 124),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, barWidth, 124),
                ViewboxUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }
    }

    private void GridChanged()
    {
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(GridBrush));
    }

    private static string Clock(long samples, int rate = 44100) => $"{(int)(samples / rate / 60)}:{samples / (double)rate % 60:00.0}";

    public string SignatureLabel => $"{_session.Engine.BeatsPerBar}/{_session.Engine.BeatUnit}";

    public void CycleSignature()
    {
        var engine = _session.Engine;
        var index = Array.IndexOf(Signatures, (engine.BeatsPerBar, engine.BeatUnit));
        (engine.BeatsPerBar, engine.BeatUnit) = Signatures[(index + 1) % Signatures.Length];
        OnPropertyChanged(nameof(SignatureLabel));
        GridChanged();
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

    public void ImportAudio(string path)
    {
        if (IsRecording)
        {
            Message = "Stop recording before importing.";
            return;
        }

        try
        {
            Message = "";
            var track = _session.ImportAudio(path);
            Notice = $"Imported {track.Name}";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public void ExportStems(string folder)
    {
        try
        {
            var files = _session.ExportStems(folder);
            Message = "";
            Notice = $"Exported {files.Count} stems to {folder}";
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
            GridChanged();
            Message = "";
            Notice = $"Opened {Path.GetFileName(Path.GetDirectoryName(path))}";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    private object? _selection;

    private DispatcherTimer? _saveTimer;

    private void OnHistoryChanged(EditChange change)
    {
        EditHistoryChanged?.Invoke();
        if (change.Kind == EditKind.Mix)
        {
            ScheduleSave();
            return;
        }

        if (change.Kind == EditKind.Tracks || change.Replayed)
        {
            RebuildTracks();
        }
        else
        {
            RefreshTimeline();
        }

        _session.Save();
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick -= SaveTick;
        _saveTimer.Tick += SaveTick;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        _session.Save();
    }

    public void Undo()
    {
        if (IsRecording)
        {
            return;
        }

        var name = _session.History.UndoName;
        if (name is null)
        {
            Notice = "Nothing to undo";
            return;
        }

        _session.History.Undo();
        Notice = $"Undid: {name.ToLowerInvariant()}";
    }

    public void Redo()
    {
        if (IsRecording)
        {
            return;
        }

        var name = _session.History.RedoName;
        if (name is null)
        {
            Notice = "Nothing to redo";
            return;
        }

        _session.History.Redo();
        Notice = $"Redid: {name.ToLowerInvariant()}";
    }

    public void Select(object? model)
    {
        _selection = model;
        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var track in Tracks)
        {
            foreach (var clip in track.Clips)
            {
                clip.Selected = ReferenceEquals(clip.Model, _selection);
            }

            foreach (var clip in track.MidiClips)
            {
                clip.Selected = ReferenceEquals(clip.Model, _selection);
            }
        }
    }

    private long PixelsToSamplesClamped(double pixels) => Math.Max(0, PixelsToSamples(pixels));

    public int RowAt(double y)
    {
        var top = 26.0;
        for (var i = 0; i < Tracks.Count; i++)
        {
            top += Tracks[i].RowHeight;
            if (y < top)
            {
                return i;
            }
        }

        return Tracks.Count;
    }

    public (TrackViewModel Track, int Lane, double RowTop)? StripAt(double y)
    {
        var top = 26.0;
        foreach (var track in Tracks)
        {
            if (y >= top && y < top + track.RowHeight)
            {
                if (!track.IsMultiLane)
                {
                    return null;
                }

                var offset = y - top;
                for (var lane = 0; lane < track.LaneCount; lane++)
                {
                    var rowTop = TrackViewModel.RowTop(track.LaneCount, lane);
                    if (offset >= rowTop && offset < rowTop + 12)
                    {
                        return (track, lane, rowTop);
                    }
                }

                return null;
            }

            top += track.RowHeight;
        }

        return null;
    }

    public void CompTake(TrackViewModel track, int lane, double fromPixel, double toPixel)
    {
        if (IsRecording || _session.Engine.SampleRate == 0)
        {
            return;
        }

        var takes = track.Model.Clips.Where(c => c.Lane == lane).ToList();
        if (takes.Count == 0)
        {
            return;
        }

        long start;
        long end;
        if (Math.Abs(toPixel - fromPixel) < 4)
        {
            start = takes.Min(c => c.StartSample);
            end = takes.Max(c => c.EndSample);
        }
        else
        {
            start = SnapSamples(PixelsToSamplesClamped(Math.Min(fromPixel, toPixel)));
            end = SnapSamples(PixelsToSamplesClamped(Math.Max(fromPixel, toPixel)));
        }

        if (end > start)
        {
            _session.CompTake(track.Model, lane, start, end);
        }
    }

    public void CommitClipMove(ClipViewModel vm, double leftPixels, int row)
    {
        if (IsRecording)
        {
            vm.Reset();
            return;
        }

        var clip = vm.Model;
        var from = vm.Owner.Model;
        var target = row >= 0 && row < Tracks.Count && !Tracks[row].IsMidi ? Tracks[row].Model : from;
        var oldStart = clip.StartSample;
        var newStart = SnapSamples(PixelsToSamplesClamped(leftPixels));
        if (target == from && newStart == oldStart)
        {
            vm.Reset();
            return;
        }

        var index = from.Clips.IndexOf(clip);
        _session.Edit(
            "Move clip",
            () =>
            {
                if (target != from)
                {
                    from.Clips.Remove(clip);
                    target.AddClip(clip);
                }

                clip.StartSample = newStart;
            },
            () =>
            {
                if (target != from)
                {
                    target.Clips.Remove(clip);
                    from.InsertClip(index, clip);
                }

                clip.StartSample = oldStart;
            });
    }

    public void CommitClipTrim(ClipViewModel vm, double leftPixels, double widthPixels)
    {
        if (IsRecording)
        {
            vm.Reset();
            return;
        }

        var playback = vm.Model.Playback;
        var oldStart = playback.StartSample;
        var oldOffset = playback.Offset;
        var oldLength = playback.Length;
        var earliest = Math.Max(0, oldStart - oldOffset);
        var latestEnd = oldStart - oldOffset + playback.Samples.Length;
        var newStart = Math.Clamp(SnapSamples(PixelsToSamplesClamped(leftPixels)), earliest, oldStart + oldLength - 1);
        var newEnd = Math.Clamp(SnapSamples(PixelsToSamplesClamped(leftPixels + widthPixels)), newStart + 1, latestEnd);
        var newOffset = oldOffset + (newStart - oldStart);
        var newLength = newEnd - newStart;
        if (newStart == oldStart && newLength == oldLength)
        {
            vm.Reset();
            return;
        }

        _session.Edit(
            "Trim clip",
            () =>
            {
                playback.Offset = newOffset;
                playback.StartSample = newStart;
                playback.Length = newLength;
            },
            () =>
            {
                playback.Offset = oldOffset;
                playback.StartSample = oldStart;
                playback.Length = oldLength;
            });
    }

    public void CommitMidiMove(MidiClipViewModel vm, double leftPixels, int row)
    {
        if (IsRecording)
        {
            vm.Reset();
            return;
        }

        var clip = vm.Model;
        var from = vm.Owner.Model;
        var target = row >= 0 && row < Tracks.Count && Tracks[row].IsMidi ? Tracks[row].Model : from;
        var oldShift = clip.Shift;
        var newShift = oldShift + (SnapSamples(PixelsToSamplesClamped(leftPixels)) - clip.StartSample);
        if (target == from && newShift == oldShift)
        {
            vm.Reset();
            return;
        }

        var index = from.MidiClips.IndexOf(clip);
        _session.Edit(
            "Move MIDI clip",
            () =>
            {
                if (target != from)
                {
                    from.MidiClips.Remove(clip);
                    target.AddMidiClip(clip);
                }

                clip.Shift = newShift;
            },
            () =>
            {
                if (target != from)
                {
                    target.MidiClips.Remove(clip);
                    from.InsertMidiClip(index, clip);
                }

                clip.Shift = oldShift;
            });
    }

    public void SplitAtPlayhead()
    {
        if (IsRecording)
        {
            return;
        }

        var position = _session.Engine.Position;
        foreach (var track in OrderedForSplit())
        {
            var audio = track.Model.Clips.FirstOrDefault(c => position > c.StartSample + 100 && position < c.EndSample - 100 && Wanted(c));
            if (audio is not null)
            {
                SplitAudio(track.Model, audio, position);
                return;
            }

            var midi = track.Model.MidiClips.FirstOrDefault(c => position > c.StartSample && position < c.EndSample && Wanted(c));
            if (midi is not null)
            {
                SplitMidi(track.Model, midi, position);
                return;
            }
        }

        Message = "Nothing to split. Move the playhead over a clip first.";
    }

    private bool Wanted(object clip) => _selection is null || ReferenceEquals(_selection, clip);

    private IEnumerable<TrackViewModel> OrderedForSplit() => Tracks;

    private void SplitAudio(Track track, Clip clip, long position)
    {
        var playback = clip.Playback;
        var originalLength = playback.Length;
        var firstLength = position - playback.StartSample;
        var right = clip.Copy(position, playback.Offset + firstLength, originalLength - firstLength);
        var index = track.Clips.IndexOf(clip);
        _selection = right;
        _session.Edit(
            "Split clip",
            () =>
            {
                playback.Length = firstLength;
                track.InsertClip(index + 1, right);
            },
            () =>
            {
                track.Clips.Remove(right);
                playback.Length = originalLength;
            });
    }

    private void SplitMidi(Track track, MidiClip clip, long position)
    {
        var (left, right) = clip.Split(position);
        if (left is null || right is null)
        {
            Message = "Nothing to split there.";
            return;
        }

        var index = track.MidiClips.IndexOf(clip);
        _selection = right;
        _session.Edit(
            "Split MIDI clip",
            () =>
            {
                track.MidiClips.Remove(clip);
                track.InsertMidiClip(index, left);
                track.InsertMidiClip(index + 1, right);
            },
            () =>
            {
                track.MidiClips.Remove(left);
                track.MidiClips.Remove(right);
                track.InsertMidiClip(index, clip);
            });
    }

    private static readonly int[] Divisions = [4, 8, 16, 32];
    private int _quantizeIndex = 2;

    public string QuantizeGridLabel => $"1/{Divisions[_quantizeIndex]}";

    public void CycleQuantizeGrid()
    {
        _quantizeIndex = (_quantizeIndex + 1) % Divisions.Length;
        OnPropertyChanged(nameof(QuantizeGridLabel));
    }

    public void QuantizeSelection()
    {
        if (IsRecording)
        {
            return;
        }

        if (_selection is not MidiClip clip || Tracks.Select(t => t.Model).FirstOrDefault(t => t.MidiClips.Contains(clip)) is not { } track)
        {
            Message = "Select a MIDI clip first.";
            return;
        }

        var engine = _session.Engine;
        var grid = (long)(engine.SamplesPerBeat * engine.BeatUnit / Divisions[_quantizeIndex]);
        var quantized = clip.Quantize(grid);
        var index = track.MidiClips.IndexOf(clip);
        _selection = quantized;
        Message = "";
        _session.Edit(
            "Quantize",
            () =>
            {
                track.MidiClips.Remove(clip);
                track.InsertMidiClip(index, quantized);
            },
            () =>
            {
                track.MidiClips.Remove(quantized);
                track.InsertMidiClip(index, clip);
            });
        Notice = $"Quantized to {QuantizeGridLabel}";
    }

    public sealed record AnalysisReport(string Title, TempoResult? Tempo, KeyResult? Key, string Message);

    private object? ClipAtPlayheadOrFirst()
    {
        var position = _session.Engine.Position;
        var audio = Tracks.SelectMany(t => t.Model.Clips).ToList();
        var midi = Tracks.SelectMany(t => t.Model.MidiClips).ToList();
        return (object?)audio.FirstOrDefault(c => position >= c.StartSample && position < c.EndSample)
            ?? midi.FirstOrDefault(c => position >= c.StartSample && position < c.EndSample)
            ?? (object?)audio.FirstOrDefault()
            ?? midi.FirstOrDefault();
    }

    public async Task<AnalysisReport> AnalyzeSelectionAsync()
    {
        var rate = _session.Engine.SampleRate;
        if (rate == 0)
        {
            return new AnalysisReport("No audio device", null, null, "Open an audio device first.");
        }

        var target = _selection ?? ClipAtPlayheadOrFirst();
        switch (target)
        {
            case Clip clip:
            {
                var playback = clip.Playback;
                var samples = new float[playback.Length];
                Array.Copy(playback.Samples, playback.Offset, samples, 0, samples.Length);
                var (tempo, key) = await Task.Run(() => (AudioAnalyzer.DetectTempo(samples, rate), AudioAnalyzer.DetectKey(samples, rate)));
                var message = tempo is null && key is null ? "That clip is too short to analyze. Try at least ten seconds." : "";
                return new AnalysisReport("Audio clip", tempo, key, message);
            }

            case MidiClip midi:
            {
                var key = AudioAnalyzer.DetectKey(midi.Notes());
                return new AnalysisReport("MIDI clip", null, key, key is null ? "There are no notes to analyze." : "Tempo can only be detected from audio.");
            }

            default:
                return new AnalysisReport("Nothing to analyze", null, null, "Record or import something first, or click a clip.");
        }
    }

    public event Action? EditHistoryChanged;

    public void EditMidiNotes(Track track, int index, IEnumerable<MidiNoteData> notes)
    {
        if (IsRecording)
        {
            return;
        }

        var original = track.MidiClips[index];
        var replacement = original.WithNotes(notes);
        if (ReferenceEquals(_selection, original))
        {
            _selection = replacement;
        }

        _session.ReplaceMidiClip(track, index, replacement, "Edit notes");
    }

    public (TrackViewModel Track, MidiClip Clip)? FindMidiClipForEditing()
    {
        var position = _session.Engine.Position;
        var clips = Tracks.SelectMany(t => t.Model.MidiClips.Select(c => (Track: t, Clip: c))).ToList();
        if (clips.Count == 0)
        {
            return null;
        }

        return clips.FirstOrDefault(c => ReferenceEquals(c.Clip, _selection)) is { Clip: not null } selected && selected.Track is not null
            ? selected
            : clips.FirstOrDefault(c => position >= c.Clip.StartSample && position < c.Clip.EndSample) is { Clip: not null } under && under.Track is not null
                ? under
                : clips[0];
    }

    public void ShowMessage(string text) => Message = text;

    public void DeleteSelection()
    {
        if (IsRecording || _selection is null)
        {
            return;
        }

        foreach (var track in Tracks.Select(t => t.Model))
        {
            if (_selection is Clip clip && track.Clips.IndexOf(clip) is >= 0 and var index)
            {
                _selection = null;
                _session.Edit("Delete clip", () => track.Clips.Remove(clip), () => track.InsertClip(index, clip));
                return;
            }

            if (_selection is MidiClip midi && track.MidiClips.IndexOf(midi) is >= 0 and var midiIndex)
            {
                _selection = null;
                _session.Edit("Delete MIDI clip", () => track.MidiClips.Remove(midi), () => track.InsertMidiClip(midiIndex, midi));
                return;
            }
        }
    }

    public void DuplicateSelection()
    {
        if (IsRecording || _selection is null)
        {
            return;
        }

        foreach (var track in Tracks.Select(t => t.Model))
        {
            if (_selection is Clip clip && track.Clips.IndexOf(clip) is >= 0 and var index)
            {
                var copy = clip.Copy(clip.EndSample, clip.Playback.Offset, clip.Length);
                _selection = copy;
                _session.Edit("Duplicate clip", () => track.InsertClip(index + 1, copy), () => track.Clips.Remove(copy));
                return;
            }

            if (_selection is MidiClip midi && track.MidiClips.IndexOf(midi) is >= 0 and var midiIndex)
            {
                var copy = midi.Copy(midi.EndSample - midi.StartSample);
                _selection = copy;
                _session.Edit("Duplicate MIDI clip", () => track.InsertMidiClip(midiIndex + 1, copy), () => track.MidiClips.Remove(copy));
                return;
            }
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

    private readonly System.Windows.Threading.Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<int> _heldNotes = [];
    private string _chord = "";

    public string ChordText
    {
        get => _chord;
        private set => SetField(ref _chord, value);
    }

    private void OnNoteForChord(byte note, byte velocity)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnNoteForChord(note, velocity));
            return;
        }

        if (velocity > 0)
        {
            _heldNotes.Add(note);
        }
        else
        {
            _heldNotes.Remove(note);
        }

        RefreshChord();
    }

    private void RefreshChord()
    {
        var pitches = _heldNotes.AsEnumerable();
        var engine = _session.Engine;
        if (engine.IsPlaying)
        {
            var position = engine.Position;
            pitches = pitches.Concat(Tracks.SelectMany(t => t.Model.MidiClips).Where(c => !c.Mute).SelectMany(c => c.PitchesAt(position)));
        }

        ChordText = ChordDetector.Name(pitches) ?? "";
    }

    public bool ScreenKeyboardOpen { get; set; }

    public event Action<byte, byte>? NoteActivity
    {
        add => _session.NoteActivity += value;
        remove => _session.NoteActivity -= value;
    }

    public void PlayNote(byte note, byte velocity) => _session.HandleNote(note, velocity);

    public void Sustain(bool down) => _session.HandleControl(MidiKind.Sustain, down ? 127 : 0);

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
            GridChanged();
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
        _session.AddTrackUndoable(UniqueName($"Input {input + 1}"), input);
    }

    public void AddMidiTrack()
    {
        _session.AddTrackUndoable(UniqueName("Keys"), null);
    }

    private void RemoveTrack(TrackViewModel track)
    {
        if (IsRecording)
        {
            Message = "Stop recording before removing a track.";
            return;
        }

        _session.RemoveTrackUndoable(track.Model);
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
            var vm = new TrackViewModel(model, Palette[model.ColorIndex % Palette.Length], _session.ApplyMixerState, DisarmConflicts, RemoveTrack, EditMix)
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

    private void EditMix(string name, Action doIt, Action undo, string? mergeKey) =>
        _session.Edit(name, doIt, undo, EditKind.Mix, mergeKey);

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

        if (_session.HasArmedMidi && !_session.Midi.IsOpen && !ScreenKeyboardOpen)
        {
            Message = "Pick a MIDI input on a Keys track, or open the on-screen keyboard.";
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

        ApplySelection();

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
        if (engine.IsPlaying || ChordText.Length > 0)
        {
            RefreshChord();
        }

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
