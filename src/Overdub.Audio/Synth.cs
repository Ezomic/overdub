using System.Collections.Concurrent;

namespace Overdub.Audio;

public sealed class Synth
{
    private const int MaxVoices = 16;
    private const float AttackSeconds = 0.005f;
    private const float DecaySeconds = 0.15f;
    private const float SustainLevel = 0.6f;
    private const float ReleaseSeconds = 0.3f;

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<(int Note, int Velocity)> _commands = new();
    private long _age;
    private int _sampleRate = 44100;

    public float Volume { get; set; } = 0.35f;

    public void Configure(int sampleRate) => _sampleRate = sampleRate;

    public void NoteOn(int note, int velocity) => _commands.Enqueue((note, Math.Clamp(velocity, 1, 127)));

    public void NoteOff(int note) => _commands.Enqueue((note, 0));

    public void AllNotesOff() => _commands.Enqueue((-1, 0));

    public void Render(float[] destination, int offset, int frames)
    {
        while (_commands.TryDequeue(out var command))
        {
            Apply(command.Note, command.Velocity);
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

    private void Apply(int note, int velocity)
    {
        if (note < 0)
        {
            for (var v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Stage != Stage.Idle)
                {
                    _voices[v].Stage = Stage.Release;
                }
            }

            return;
        }

        if (velocity == 0)
        {
            for (var v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Note == note && _voices[v].Stage is not (Stage.Idle or Stage.Release))
                {
                    _voices[v].Stage = Stage.Release;
                }
            }

            return;
        }

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
        var step = voice.Frequency / _sampleRate;
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
    }
}
