using System.Collections.Concurrent;

namespace Overdub.Audio;

public sealed class Synth : INoteTarget
{
    private const int MaxVoices = 16;
    private const double BendSemitones = 2.0;

    private enum Command
    {
        NoteOn,
        NoteOff,
        AllOff,
        Pedal,
        Bend,
    }

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<(Command Kind, int A, int B)> _commands = new();
    private long _age;
    private int _sampleRate = 44100;
    private bool _pedal;
    private double _bend = 1.0;
    private SynthPatch _patch = SynthPatch.All[0];
    private Vst3.Vst3Plugin? _instrument;
    private readonly bool[] _pluginHeld = new bool[128];
    private float[] _pluginLeft = new float[4096];
    private float[] _pluginRight = new float[4096];

    public string PresetName => _patch.Name;

    public void SetPreset(string? name) => _patch = SynthPatch.Named(name);

    public float Volume { get; set; } = 0.35f;

    public Vst3.Vst3Plugin? Instrument { get => Volatile.Read(ref _instrument); set => Volatile.Write(ref _instrument, value); }

    public void Configure(int sampleRate) => _sampleRate = sampleRate;

    public void NoteOn(int note, int velocity) => _commands.Enqueue((Command.NoteOn, note, Math.Clamp(velocity, 1, 127)));

    public void NoteOff(int note) => _commands.Enqueue((Command.NoteOff, note, 0));

    public void AllNotesOff() => _commands.Enqueue((Command.AllOff, 0, 0));

    public void SustainPedal(bool down) => _commands.Enqueue((Command.Pedal, down ? 1 : 0, 0));

    public void PitchBend(int value) => _commands.Enqueue((Command.Bend, Math.Clamp(value, -8192, 8191), 0));

    public void Render(float[] destination, int offset, int frames)
    {
        if (Instrument is { } plugin)
        {
            RenderPlugin(plugin, destination, offset, frames);
            return;
        }

        while (_commands.TryDequeue(out var command))
        {
            Apply(command.Kind, command.A, command.B);
        }

        for (var v = 0; v < MaxVoices; v++)
        {
            ref var voice = ref _voices[v];
            if (voice.Stage == Stage.Idle)
            {
                continue;
            }

            RenderVoice(ref voice, destination, offset, frames);
        }
    }

    private void RenderPlugin(Vst3.Vst3Plugin plugin, float[] destination, int offset, int frames)
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case Command.NoteOn:
                    _pluginHeld[command.A] = true;
                    plugin.NoteOn(command.A, command.B);
                    break;
                case Command.NoteOff:
                    _pluginHeld[command.A] = false;
                    plugin.NoteOff(command.A);
                    break;
                case Command.AllOff:
                    for (var note = 0; note < _pluginHeld.Length; note++)
                    {
                        if (_pluginHeld[note])
                        {
                            _pluginHeld[note] = false;
                            plugin.NoteOff(note);
                        }
                    }

                    break;
            }
        }

        for (var done = 0; done < frames;)
        {
            var chunk = Math.Min(frames - done, _pluginLeft.Length);
            Array.Clear(_pluginLeft, 0, chunk);
            Array.Clear(_pluginRight, 0, chunk);
            plugin.Process(_pluginLeft, _pluginRight, chunk);
            for (var i = 0; i < chunk; i++)
            {
                destination[offset + done + i] += (_pluginLeft[i] + _pluginRight[i]) * 0.5f;
            }

            done += chunk;
        }
    }

    private void Apply(Command kind, int a, int b)
    {
        switch (kind)
        {
            case Command.AllOff:
                _pedal = false;
                _bend = 1.0;
                for (var v = 0; v < MaxVoices; v++)
                {
                    ReleaseVoice(ref _voices[v]);
                }

                break;
            case Command.Pedal:
                _pedal = a != 0;
                if (!_pedal)
                {
                    for (var v = 0; v < MaxVoices; v++)
                    {
                        if (_voices[v].PedalHeld)
                        {
                            ReleaseVoice(ref _voices[v]);
                        }
                    }
                }

                break;
            case Command.Bend:
                _bend = Math.Pow(2, a / 8192.0 * BendSemitones / 12.0);
                break;
            case Command.NoteOff:
                for (var v = 0; v < MaxVoices; v++)
                {
                    if (_voices[v].Note != a || _voices[v].Stage is Stage.Idle or Stage.Release)
                    {
                        continue;
                    }

                    if (_pedal)
                    {
                        _voices[v].PedalHeld = true;
                    }
                    else
                    {
                        ReleaseVoice(ref _voices[v]);
                    }
                }

                break;
            default:
                StartVoice(a, b);
                break;
        }
    }

    private static void ReleaseVoice(ref Voice voice)
    {
        voice.PedalHeld = false;
        if (voice.Stage != Stage.Idle)
        {
            voice.Stage = Stage.Release;
        }
    }

    private void StartVoice(int note, int velocity)
    {
        var slot = 0;
        for (var v = 0; v < MaxVoices; v++)
        {
            if (_voices[v].Stage == Stage.Idle)
            {
                slot = v;
                break;
            }

            if (_voices[v].Age < _voices[slot].Age)
            {
                slot = v;
            }
        }

        _voices[slot] = new Voice
        {
            Note = note,
            Frequency = 440.0 * Math.Pow(2, (note - 69) / 12.0),
            Velocity = velocity / 127f,
            Stage = Stage.Attack,
            Age = ++_age,
            Patch = _patch,
            Droop = 1f,
        };
    }

    private void RenderVoice(ref Voice voice, float[] destination, int offset, int frames)
    {
        var patch = voice.Patch;
        var table = patch.Table;
        var detunes = patch.Detunes;
        var step = voice.Frequency * _bend / _sampleRate;
        var attackStep = 1f / (patch.Attack * _sampleRate);
        var decayStep = (1f - patch.Sustain) / (patch.Decay * _sampleRate);
        var releaseStep = 1f / (patch.Release * _sampleRate);
        var droopFactor = patch.HeldDecay <= 0f ? 1f : (float)Math.Exp(-patch.HeldDecay / _sampleRate);
        var cutoff = patch.Cutoff * (1f - patch.VelocityBrightness + (patch.VelocityBrightness * voice.Velocity));
        cutoff = Math.Clamp(cutoff, 0.01f, 1f);
        var output = patch.Gain * voice.Velocity * Volume;

        for (var i = 0; i < frames; i++)
        {
            switch (voice.Stage)
            {
                case Stage.Attack:
                    voice.Level += attackStep;
                    if (voice.Level >= 1f)
                    {
                        voice.Level = 1f;
                        voice.Stage = Stage.Decay;
                    }

                    break;
                case Stage.Decay:
                    voice.Level -= decayStep;
                    voice.Droop *= droopFactor;
                    if (voice.Level <= patch.Sustain)
                    {
                        voice.Level = patch.Sustain;
                        voice.Stage = Stage.Sustain;
                    }

                    break;
                case Stage.Sustain:
                    voice.Droop *= droopFactor;
                    if (voice.Level * voice.Droop < 0.0005f)
                    {
                        voice.Stage = Stage.Idle;
                        return;
                    }

                    break;
                case Stage.Release:
                    voice.Level -= releaseStep;
                    if (voice.Level <= 0f)
                    {
                        voice.Stage = Stage.Idle;
                        return;
                    }

                    break;
            }

            var sample = 0f;
            for (var d = 0; d < detunes.Length; d++)
            {
                var phase = d switch
                {
                    0 => voice.Phase,
                    1 => voice.PhaseB,
                    _ => voice.Phase2,
                };
                var position = phase * 2048;
                var index = (int)position;
                var fraction = (float)(position - index);
                sample += table[index] + ((table[index + 1] - table[index]) * fraction);
            }

            voice.Filter += cutoff * (sample - voice.Filter);
            destination[offset + i] += voice.Filter * voice.Level * voice.Droop * output;

            voice.Phase += step * detunes[0];
            voice.Phase -= Math.Floor(voice.Phase);
            if (detunes.Length > 1)
            {
                voice.PhaseB += step * detunes[1];
                voice.PhaseB -= Math.Floor(voice.PhaseB);
            }

            if (detunes.Length > 2)
            {
                voice.Phase2 += step * detunes[2];
                voice.Phase2 -= Math.Floor(voice.Phase2);
            }
        }
    }

    private enum Stage
    {
        Idle,
        Attack,
        Decay,
        Sustain,
        Release,
    }

    private struct Voice
    {
        public int Note;
        public double Frequency;
        public double Phase;
        public double PhaseB;
        public float Velocity;
        public float Level;
        public float Filter;
        public long Age;
        public Stage Stage;
        public bool PedalHeld;
        public SynthPatch Patch;
        public double Phase2;
        public float Droop;
    }
}
