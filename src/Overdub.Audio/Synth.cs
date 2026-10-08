using System.Collections.Concurrent;

namespace Overdub.Audio;

public sealed class Synth
{
    private const int MaxVoices = 16;
    private const float AttackSeconds = 0.005f;
    private const float DecaySeconds = 0.15f;
    private const float SustainLevel = 0.6f;
    private const float ReleaseSeconds = 0.3f;
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

    public float Volume { get; set; } = 0.35f;

    public void Configure(int sampleRate) => _sampleRate = sampleRate;

    public void NoteOn(int note, int velocity) => _commands.Enqueue((Command.NoteOn, note, Math.Clamp(velocity, 1, 127)));

    public void NoteOff(int note) => _commands.Enqueue((Command.NoteOff, note, 0));

    public void AllNotesOff() => _commands.Enqueue((Command.AllOff, 0, 0));

    public void SustainPedal(bool down) => _commands.Enqueue((Command.Pedal, down ? 1 : 0, 0));

    public void PitchBend(int value) => _commands.Enqueue((Command.Bend, Math.Clamp(value, -8192, 8191), 0));

    public void Render(float[] destination, int offset, int frames)
    {
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
                        if (_voices[v].Held)
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
                        _voices[v].Held = true;
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
        voice.Held = false;
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
        };
    }

    private void RenderVoice(ref Voice voice, float[] destination, int offset, int frames)
    {
        var step = voice.Frequency * _bend / _sampleRate;
        var attackStep = 1f / (AttackSeconds * _sampleRate);
        var decayStep = (1f - SustainLevel) / (DecaySeconds * _sampleRate);
        var releaseStep = 1f / (ReleaseSeconds * _sampleRate);
        const float cutoff = 0.25f;

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
                    if (voice.Level <= SustainLevel)
                    {
                        voice.Level = SustainLevel;
                        voice.Stage = Stage.Sustain;
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

            var saw = (float)((2 * voice.Phase) - 1);
            var detuned = (float)((2 * voice.PhaseB) - 1);
            voice.Filter += cutoff * (((saw + detuned) * 0.5f) - voice.Filter);
            destination[offset + i] += voice.Filter * voice.Level * voice.Velocity * Volume;

            voice.Phase += step;
            voice.PhaseB += step * 1.006;
            voice.Phase -= Math.Floor(voice.Phase);
            voice.PhaseB -= Math.Floor(voice.PhaseB);
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
        public bool Held;
    }
}
