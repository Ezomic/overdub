using System.Collections.ObjectModel;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public static class Timeline
{
    public const double PixelsPerSecond = 80;
}

public sealed class ClipViewModel : ObservableObject
{
    private double _left;
    private double _width;
    private bool _selected;

    public ClipViewModel(Clip clip, int sampleRate, Brush brush, TrackViewModel owner)
    {
        Model = clip;
        SampleRate = sampleRate;
        Brush = brush;
        Owner = owner;
        Peaks = clip.ComputePeaks();
        Reset();
    }

    public Clip Model { get; }
    public int SampleRate { get; }
    public Brush Brush { get; }
    public TrackViewModel Owner { get; }
    public float[] Peaks { get; }

    public double Left
    {
        get => _left;
        set => SetField(ref _left, value);
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    public bool Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public double ToPixels(long samples) => (double)samples / SampleRate * Timeline.PixelsPerSecond;

    public void Reset()
    {
        Left = ToPixels(Model.StartSample);
        Width = Math.Max(2, ToPixels(Model.Length));
    }
}

public sealed class MidiClipViewModel : ObservableObject
{
    private const double Height = 108;

    private double _left;
    private double _width;
    private bool _selected;

    public MidiClipViewModel(MidiClip clip, int sampleRate, Brush brush, TrackViewModel owner)
    {
        Model = clip;
        Brush = brush;
        Owner = owner;
        SampleRate = sampleRate;
        var secondsToPixels = Timeline.PixelsPerSecond / sampleRate;
        var notes = clip.Notes();
        if (notes.Count == 0)
        {
            Notes = [];
        }
        else
        {
            var low = notes.Min(n => n.Pitch);
            var high = notes.Max(n => n.Pitch);
            var rows = high - low + 1;
            var rowHeight = Math.Clamp((Height - 8) / rows, 3, 9);
            var top = (Height - (rows * rowHeight)) / 2;
            Notes = notes.Select(n => new NoteRect(
                (n.Start - clip.StartSample) * secondsToPixels,
                top + ((high - n.Pitch) * rowHeight),
                Math.Max(2, (n.End - n.Start) * secondsToPixels),
                Math.Max(2, rowHeight - 1))).ToArray();
        }

        Reset();
    }

    public MidiClip Model { get; }
    public Brush Brush { get; }
    public TrackViewModel Owner { get; }
    public int SampleRate { get; }
    public NoteRect[] Notes { get; }

    public double Left
    {
        get => _left;
        set => SetField(ref _left, value);
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    public bool Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public void Reset()
    {
        var toPixels = Timeline.PixelsPerSecond / SampleRate;
        Left = Model.StartSample * toPixels;
        Width = Math.Max(8, (Model.EndSample - Model.StartSample) * toPixels);
    }
}

public sealed class TrackViewModel(Track model, Brush color, Action onMixChanged, Action<TrackViewModel> onArmed, Action<TrackViewModel> onRemove) : ObservableObject
{
    private bool _editing;
    private bool _removePending;
    private RelayCommand? _remove;
    private double _level;
    private bool _clipped;
    private string _midiLabel = "No MIDI input";

    public Track Model { get; } = model;
    public Brush Color { get; } = color;
    public string Name
    {
        get => Model.Name;
        set
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                Model.Name = trimmed;
            }

            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _editing;
        set
        {
            if (SetField(ref _editing, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !_editing;
    public string RemoveLabel => _removePending ? "Remove?" : "✕";
    public System.Windows.Input.ICommand RemoveCommand => _remove ??= new RelayCommand(RemoveClicked);

    public void ResetRemove()
    {
        _removePending = false;
        OnPropertyChanged(nameof(RemoveLabel));
    }

    private void RemoveClicked()
    {
        if (!_removePending)
        {
            _removePending = true;
            OnPropertyChanged(nameof(RemoveLabel));
            return;
        }

        onRemove(this);
    }
    public bool HasInput => true;
    public bool IsMidi => Model.IsMidi;
    public bool IsAudio => !Model.IsMidi;
    public string InputText => Model.Input is { } input ? $"Input {input + 1}, instrument" : "MIDI, built-in synth";
    public ObservableCollection<MidiClipViewModel> MidiClips { get; } = [];
    public System.Windows.Input.ICommand? CycleMidi { get; set; }

    public string MidiLabel
    {
        get => _midiLabel;
        set => SetField(ref _midiLabel, value);
    }
    public ObservableCollection<ClipViewModel> Clips { get; } = [];

    public bool Armed
    {
        get => Model.Armed;
        set
        {
            Model.Armed = value;
            if (value)
            {
                onArmed(this);
            }

            OnPropertyChanged();
        }
    }

    public bool Mute
    {
        get => Model.Mute;
        set
        {
            Model.Mute = value;
            OnPropertyChanged();
        }
    }

    public bool Solo
    {
        get => Model.Solo;
        set
        {
            Model.Solo = value;
            OnPropertyChanged();
        }
    }

    public double Gain
    {
        get => Model.Gain;
        set
        {
            Model.Gain = (float)value;
            onMixChanged();
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainText));
        }
    }

    public double Pan
    {
        get => Model.Pan;
        set
        {
            Model.Pan = (float)value;
            onMixChanged();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanText));
        }
    }

    public string GainText => Model.Gain <= 0.0001f ? "-inf dB" : $"{20 * Math.Log10(Model.Gain):+0.0;-0.0;0.0} dB";

    public string PanText => Math.Abs(Model.Pan) < 0.005f ? "Center" : Model.Pan < 0 ? $"L {-Model.Pan * 100:0}" : $"R {Model.Pan * 100:0}";

    public double Level
    {
        get => _level;
        private set => SetField(ref _level, value);
    }

    public bool Clipped
    {
        get => _clipped;
        private set => SetField(ref _clipped, value);
    }

    public void UpdateMidiMeter(float activity) => Level = Math.Max(activity, Level * 0.88);

    public void UpdateMeter(AsioEngine engine)
    {
        if (Model.Input is not { } input || input >= engine.InputCount)
        {
            return;
        }

        var peak = engine.ReadPeak(input);
        var db = peak <= 0.00001f ? -60 : 20 * Math.Log10(peak);
        Level = Math.Max(Math.Clamp((db + 60) / 60, 0, 1), Level * 0.88);
        if (engine.ReadClipped(input))
        {
            Clipped = true;
        }
        else if (Level < 0.5)
        {
            Clipped = false;
        }
    }

    public void NotifyMixChanged()
    {
        OnPropertyChanged(nameof(Mute));
        OnPropertyChanged(nameof(Solo));
    }

    public void RefreshClips(int sampleRate)
    {
        Clips.Clear();
        foreach (var clip in Model.Clips)
        {
            Clips.Add(new ClipViewModel(clip, sampleRate, Color, this));
        }

        MidiClips.Clear();
        foreach (var clip in Model.MidiClips)
        {
            MidiClips.Add(new MidiClipViewModel(clip, sampleRate, Color, this));
        }
    }
}
