using System.Collections.ObjectModel;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public static class Timeline
{
    public const double PixelsPerSecond = 80;
}

public sealed class ClipViewModel(Clip clip, int sampleRate, Brush brush)
{
    public double Left { get; } = (double)clip.StartSample / sampleRate * Timeline.PixelsPerSecond;
    public double Width { get; } = Math.Max(2, (double)clip.Length / sampleRate * Timeline.PixelsPerSecond);
    public float[] Peaks { get; } = clip.Peaks;
    public Brush Brush { get; } = brush;
}

public sealed class MidiClipViewModel
{
    private const double Height = 80;

    public MidiClipViewModel(MidiClip clip, int sampleRate, Brush brush)
    {
        Brush = brush;
        var secondsToPixels = Timeline.PixelsPerSecond / sampleRate;
        Left = clip.StartSample * secondsToPixels;
        Width = Math.Max(8, (clip.EndSample - clip.StartSample) * secondsToPixels);
        var notes = clip.Notes();
        if (notes.Count == 0)
        {
            Notes = [];
            return;
        }

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

    public double Left { get; }
    public double Width { get; }
    public NoteRect[] Notes { get; }
    public Brush Brush { get; }
}

public sealed class TrackViewModel(Track model, Brush color) : ObservableObject
{
    private double _level;
    private bool _clipped;
    private string _midiLabel = "No MIDI input";

    public Track Model { get; } = model;
    public Brush Color { get; } = color;
    public string Name => Model.Name;
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
            Clips.Add(new ClipViewModel(clip, sampleRate, Color));
        }

        MidiClips.Clear();
        foreach (var clip in Model.MidiClips)
        {
            MidiClips.Add(new MidiClipViewModel(clip, sampleRate, Color));
        }
    }
}
