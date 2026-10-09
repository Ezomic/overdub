namespace Overdub.Audio;

public sealed class MachineLane(MachineRole role)
{
    private readonly Synth _synth = new();
    private readonly PluckSynth _pluck = new();
    private readonly SfzPlayer _player = new();
    private string? _presetName;
    private bool _usePlayer;
    private int _sampleRate = 44100;
    private bool _usePluck;
    private bool _usePlugin;

    public MachineRole Role { get; } = role;
    public MidiSequencer Sequencer { get; } = new();
    public MidiClip[] Clips { get; set; } = [];
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public INoteTarget Voice => _usePlugin ? _synth : _usePlayer ? _player : _usePluck ? _pluck : _synth;

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
        _player.Configure(sampleRate);
    }

    public void SetPreset(string? name, bool waitForSamples = false)
    {
        if (name == _presetName && !waitForSamples)
        {
            return;
        }

        _presetName = name;
        var sfz = SfzInstrument.FindPath(name);
        _usePlayer = sfz is not null;
        if (sfz is not null)
        {
            _player.SetInstrument(null);
            if (waitForSamples)
            {
                _player.SetInstrument(SfzInstrument.Load(sfz));
            }
            else
            {
                _ = Task.Run(() =>
                {
                    var instrument = SfzInstrument.Load(sfz);
                    if (_presetName == name)
                    {
                        _player.SetInstrument(instrument);
                    }
                });
            }

            return;
        }

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
        _player.AllNotesOff();
    }

    public static INoteTarget CreateVoice(MachineRole role, string? preset, int sampleRate, Vst3.Vst3Plugin? instrument = null)
    {
        var lane = new MachineLane(role);
        lane.Configure(sampleRate);
        lane.SetPreset(preset, waitForSamples: true);
        lane.SetInstrument(instrument);
        return lane.Voice;
    }
}

public sealed record MachineMix(MachineRole Role, IReadOnlyList<MidiClip> Clips, float Gain, float Pan, string Preset, Vst3.Vst3Plugin? Instrument = null);
