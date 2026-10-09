namespace Overdub.Audio;

public sealed class MachineLane(MachineRole role)
{
    private readonly Synth _synth = new();
    private readonly PluckSynth _pluck = new();
    private int _sampleRate = 44100;
    private bool _usePluck;
    private bool _usePlugin;

    public MachineRole Role { get; } = role;
    public MidiSequencer Sequencer { get; } = new();
    public MidiClip[] Clips { get; set; } = [];
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public INoteTarget Voice => _usePlugin ? _synth : _usePluck ? _pluck : _synth;

    public void SetInstrument(Vst3.Vst3Plugin? plugin)
    {
        _synth.Instrument = plugin;
        _usePlugin = plugin is not null;
    }

    public void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _synth.Configure(sampleRate);
        _pluck.Configure(sampleRate);
    }

    public void SetPreset(string? name)
    {
        var character = PluckSynth.Find(name);
        if (character is not null)
        {
            _pluck.SetCharacter(character);
            _usePluck = true;
            return;
        }

        _usePluck = false;
        _synth.SetPreset(name == "Synth pluck" ? "Pluck" : name == "Synth bass" ? "Bass" : name == "Synth lead" ? "Lead" : name);
    }

    public void AllNotesOff()
    {
        _synth.AllNotesOff();
        _pluck.AllNotesOff();
    }

    public static INoteTarget CreateVoice(MachineRole role, string? preset, int sampleRate, Vst3.Vst3Plugin? instrument = null)
    {
        var lane = new MachineLane(role);
        lane.Configure(sampleRate);
        lane.SetPreset(preset);
        lane.SetInstrument(instrument);
        return lane.Voice;
    }
}

public sealed record MachineMix(MachineRole Role, IReadOnlyList<MidiClip> Clips, float Gain, float Pan, string Preset, Vst3.Vst3Plugin? Instrument = null);
