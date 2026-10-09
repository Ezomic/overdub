using System.Collections.ObjectModel;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public static class Timeline
{
    public const double PixelsPerSecond = 80;
}

public sealed class StripViewModel(double left, double top, double width, double height, Brush fill, double opacity)
{
    public double Left { get; } = left;
    public double Top { get; } = top;
    public double Width { get; } = width;
    public double Height { get; } = height;
    public Brush Fill { get; } = fill;
    public double Opacity { get; } = opacity;
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
        var lanes = owner.Model.LaneCount;
        var multi = lanes > 1;
        Top = multi ? TrackViewModel.RowTop(lanes, clip.Lane) + 14 : 8;
        ClipHeight = multi ? 40 : 108;
        Reset();
    }

    public double Top { get; }
    public double ClipHeight { get; }

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

public sealed class TrackViewModel(Track model, Brush color, Action onMixChanged, Action<TrackViewModel> onArmed, Action<TrackViewModel> onRemove, Action<string, Action, Action, string?> onEdit) : ObservableObject
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
            if (string.IsNullOrEmpty(trimmed) || trimmed == Model.Name)
            {
                OnPropertyChanged();
                return;
            }

            var old = Model.Name;
            onEdit("Rename track", () => ApplyName(trimmed), () => ApplyName(old), null);
        }
    }

    private void ApplyName(string name)
    {
        Model.Name = name;
        OnPropertyChanged(nameof(Name));
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
    public bool HasInput => !Model.IsBacking && !Model.IsDrums && Model.Machine is null or MachineRole.Lead;
    public int LaneCount => Model.LaneCount;
    public bool IsMultiLane => LaneCount > 1;
    public double RowHeight => IsMultiLane ? Math.Max(124, 10 + (LaneCount * 58)) : 124;
    public string TakesText => $"{LaneCount} takes: drag a strip to choose which plays";
    public ObservableCollection<StripViewModel> Strips { get; } = [];

    public static double RowTop(int lanes, int lane) => 6 + ((lanes - 1 - lane) * 58);
    public bool IsMidi => Model.IsMidi;
    public bool IsDrums => Model.IsDrums;

    public string KitTitle => $"Kit: {DrumKit.StyleNames[Math.Clamp(Model.DrumKitStyle, 0, DrumKit.StyleNames.Length - 1)]}";

    public void NotifyKit() => OnPropertyChanged(nameof(KitTitle));
    public bool IsKeys => Model.IsKeys;
    public bool IsMachine => Model.Machine is not null;
    public bool IsLead => Model.Machine == MachineRole.Lead;
    public bool IsBass => Model.Machine == MachineRole.Bass;
    public bool IsChordMachine => Model.Machine is MachineRole.Guitar or MachineRole.Bass;
    public bool IsAudio => !Model.IsMidi;
    public string InputText => Model.IsBacking ? "Imported audio" : Model.Input is { } input ? $"Input {input + 1}, instrument" : "MIDI, built-in synth";
    public ObservableCollection<MidiClipViewModel> MidiClips { get; } = [];
    public System.Windows.Input.ICommand? CycleMidi { get; set; }

    public string Preset => Model.Preset;

    public string PresetTitle => Model.IsMidi ? SoundPrograms.DisplayTitle(Model.Preset) : Model.Preset;

    public bool CanHaveEffects => !Model.IsMidi;
    public bool HasEffects => Model.Effects.AnyEnabled;
    public EffectChain Effects => Model.Effects;

    public event Action? EffectsChanged;

    public void RaiseEffectsChanged()
    {
        OnPropertyChanged(nameof(HasEffects));
        EffectsChanged?.Invoke();
    }

    public Sends Sends => Model.Sends;

    public void SetSends(float? reverb, float? delay)
    {
        if (reverb is { } r)
        {
            Model.Sends.Reverb = Math.Clamp(r, 0, 1);
        }

        if (delay is { } d)
        {
            Model.Sends.Delay = Math.Clamp(d, 0, 1);
        }

        onMixChanged();
    }

    public static readonly (string Name, double[] Values)[] AmpPresets =
    [
        ("Clean", [0, 0.2, 1, 0, 2, 0.35, 1, 0]),
        ("Edge of breakup", [1, 0.3, 1, 1, 1, 0.4, 1, 0]),
        ("Crunch", [1, 0.5, 2, 1, 1, 0.4, 1, -1]),
        ("Lead", [2, 0.65, 1, 3, 0, 0.5, 1, -3]),
        ("High gain", [2, 0.85, 2, 1, 1, 0.6, 1, -4]),
        ("Bass", [3, 0.3, 4, -1, 1, 0.15, 1, 0]),
        ("Bass grit", [3, 0.6, 2, 2, 1, 0.3, 1, -2]),
        ("Direct, no cabinet", [0, 0.1, 0, 0, 0, 0.2, 0, 0]),
    ];

    public void ApplyChainPreset(ChainPreset preset)
    {
        var chain = Model.Effects;
        var old = chain.Effects.Select(e => ((double[])e.Values.Clone(), e.Enabled)).ToArray();
        onEdit(
            $"{preset.Name} preset",
            () =>
            {
                preset.ApplyTo(chain);
                OnPropertyChanged(nameof(HasEffects));
                EffectsChanged?.Invoke();
            },
            () =>
            {
                for (var i = 0; i < old.Length; i++)
                {
                    SetAllEffectValues(i, old[i].Item1, old[i].Enabled);
                }
            },
            null);
    }

    public void ApplyEffectPreset(int effect, string name, double[] values)
    {
        var target = Model.Effects.Effects[effect];
        var oldValues = (double[])target.Values.Clone();
        var oldEnabled = target.Enabled;
        onEdit($"{name} amp preset", () => SetAllEffectValues(effect, values, true), () => SetAllEffectValues(effect, oldValues, oldEnabled), null);
    }

    private void SetAllEffectValues(int effect, double[] values, bool enabled)
    {
        var target = Model.Effects.Effects[effect];
        for (var i = 0; i < Math.Min(values.Length, target.Values.Length); i++)
        {
            target.Set(i, values[i]);
        }

        target.Enabled = enabled;
        Model.Effects.Touch();
        OnPropertyChanged(nameof(HasEffects));
        EffectsChanged?.Invoke();
    }

    public void SetEffectEnabled(int effect, bool enabled)
    {
        var target = Model.Effects.Effects[effect];
        var old = target.Enabled;
        if (old == enabled)
        {
            return;
        }

        onEdit($"{(enabled ? "Enable" : "Disable")} {target.Name.ToLowerInvariant()}", () => ApplyEffectEnabled(effect, enabled), () => ApplyEffectEnabled(effect, old), null);
    }

    public void SetEffectValue(int effect, int parameter, double value)
    {
        var target = Model.Effects.Effects[effect];
        var clamped = Math.Clamp(value, target.Parameters[parameter].Min, target.Parameters[parameter].Max);
        var old = target.Get(parameter);
        if (Math.Abs(old - clamped) < 1e-9)
        {
            return;
        }

        onEdit($"Change {target.Name.ToLowerInvariant()}", () => ApplyEffectValue(effect, parameter, clamped), () => ApplyEffectValue(effect, parameter, old), $"fx:{Model.Id}:{effect}:{parameter}");
    }

    private void ApplyEffectEnabled(int effect, bool enabled)
    {
        Model.Effects.Effects[effect].Enabled = enabled;
        Model.Effects.Touch();
        OnPropertyChanged(nameof(HasEffects));
        EffectsChanged?.Invoke();
    }

    private void ApplyEffectValue(int effect, int parameter, double value)
    {
        Model.Effects.Effects[effect].Set(parameter, value);
        Model.Effects.Touch();
        EffectsChanged?.Invoke();
    }

    private RelayCommand? _cyclePreset;

    public System.Windows.Input.ICommand CyclePreset => _cyclePreset ??= new RelayCommand(() =>
    {
        var old = Model.Preset;
        var next = SynthPatch.Next(old);
        onEdit("Change sound", () => ApplyPreset(next), () => ApplyPreset(old), null);
    });

    public IReadOnlyList<string> PresetOptions => Model.Machine is { } role ? PluckSynth.Names(role) : SynthPatch.All.Select(p => p.Name).ToList();

    public void SetPreset(string preset)
    {
        var old = Model.Preset;
        if (string.Equals(old, preset, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var bypass = Model.Instrument.Active;
        onEdit(
            "Change sound",
            () =>
            {
                if (bypass)
                {
                    Model.Instrument.Enabled = false;
                }

                ApplyPreset(preset);
            },
            () =>
            {
                if (bypass)
                {
                    Model.Instrument.Enabled = true;
                }

                ApplyPreset(old);
            },
            null);
    }

    private void ApplyPreset(string preset)
    {
        Model.Preset = preset;
        onMixChanged();
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(PresetTitle));
    }

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
            if (value == Model.Mute)
            {
                return;
            }

            var old = Model.Mute;
            onEdit(value ? "Mute track" : "Unmute track", () => ApplyMute(value), () => ApplyMute(old), null);
        }
    }

    public bool Solo
    {
        get => Model.Solo;
        set
        {
            if (value == Model.Solo)
            {
                return;
            }

            var old = Model.Solo;
            onEdit(value ? "Solo track" : "Unsolo track", () => ApplySolo(value), () => ApplySolo(old), null);
        }
    }

    private void ApplyMute(bool value)
    {
        Model.Mute = value;
        onMixChanged();
        OnPropertyChanged(nameof(Mute));
    }

    private void ApplySolo(bool value)
    {
        Model.Solo = value;
        onMixChanged();
        OnPropertyChanged(nameof(Solo));
    }

    public double Gain
    {
        get => Model.Gain;
        set
        {
            var next = (float)value;
            if (Math.Abs(next - Model.Gain) < 0.0001f)
            {
                return;
            }

            var old = Model.Gain;
            onEdit("Change volume", () => ApplyGain(next), () => ApplyGain(old), $"vol:{Model.Id}");
        }
    }

    public double Pan
    {
        get => Model.Pan;
        set
        {
            var next = (float)value;
            if (Math.Abs(next - Model.Pan) < 0.0001f)
            {
                return;
            }

            var old = Model.Pan;
            onEdit("Change pan", () => ApplyPan(next), () => ApplyPan(old), $"pan:{Model.Id}");
        }
    }

    private void ApplyGain(float value)
    {
        Model.Gain = value;
        onMixChanged();
        OnPropertyChanged(nameof(Gain));
        OnPropertyChanged(nameof(GainText));
    }

    private void ApplyPan(float value)
    {
        Model.Pan = value;
        onMixChanged();
        OnPropertyChanged(nameof(Pan));
        OnPropertyChanged(nameof(PanText));
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

        Strips.Clear();
        if (Model.LaneCount > 1)
        {
            var scale = Timeline.PixelsPerSecond / sampleRate;
            var dim = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x42));
            dim.Freeze();
            for (var lane = 0; lane < Model.LaneCount; lane++)
            {
                Strips.Add(new StripViewModel(0, RowTop(Model.LaneCount, lane), 100000, 12, dim, 0.6));
            }

            foreach (var range in Model.ActiveRanges())
            {
                Strips.Add(new StripViewModel(range.Start * scale, RowTop(Model.LaneCount, range.Lane), Math.Max(2, (range.End - range.Start) * scale), 12, this.Color, 1.0));
            }
        }

        OnPropertyChanged(nameof(LaneCount));
        OnPropertyChanged(nameof(IsMultiLane));
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(TakesText));

        MidiClips.Clear();
        foreach (var clip in Model.MidiClips)
        {
            MidiClips.Add(new MidiClipViewModel(clip, sampleRate, Color, this));
        }
    }
}
