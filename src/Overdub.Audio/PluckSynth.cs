using System.Collections.Concurrent;

namespace Overdub.Audio;

public sealed record PluckCharacter(string Name, MachineRole Role, float Brightness, float Sustain, float Body, float PickPosition, float Excite, float Click = 0f, float Drive = 0f, float CabHz = 0f);

public sealed class PluckSynth : INoteTarget
{
    private const int MaxVoices = 14;
    private const int BufferSize = 8192;

    public static IReadOnlyList<PluckCharacter> Characters { get; } =
    [
        new("Acoustic", MachineRole.Guitar, 0.55f, 0.9966f, 0.35f, 0.14f, 0.6f, 0.15f),
        new("Plectrum", MachineRole.Guitar, 0.66f, 0.9969f, 0.38f, 0.12f, 0.78f, 0.55f),
        new("Nylon", MachineRole.Guitar, 0.3f, 0.9962f, 0.4f, 0.22f, 0.4f),
        new("Electric clean", MachineRole.Guitar, 0.6f, 0.9976f, 0.06f, 0.2f, 0.7f, 0.3f, 0f, 6500f),
        new("Electric crunch", MachineRole.Guitar, 0.62f, 0.9978f, 0.04f, 0.12f, 0.75f, 0.3f, 3.5f, 4800f),
        new("Electric lead", MachineRole.Guitar, 0.58f, 0.9986f, 0.03f, 0.12f, 0.72f, 0.2f, 9f, 4200f),
        new("Finger bass", MachineRole.Bass, 0.16f, 0.9987f, 0.0f, 0.2f, 0.3f),
        new("Pick bass", MachineRole.Bass, 0.3f, 0.9982f, 0.0f, 0.12f, 0.5f, 0.3f),
    ];

    public static IReadOnlyList<string> Names(MachineRole role) => BuiltInNames(role).Concat(SoundPrograms.Discover().Where(p => p.Fits(role)).Select(p => p.Name)).ToList();

    public static IReadOnlyList<string> BuiltInNames(MachineRole role) => role == MachineRole.Lead
        ? ["Electric lead", "Electric crunch", "Electric clean", "Plectrum", "Acoustic", "Synth lead"]
        : Characters.Where(c => c.Role == role).Select(c => c.Name).Append(role == MachineRole.Guitar ? "Synth pluck" : "Synth bass").ToList();

    public static PluckCharacter? Find(string? name) => Characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string DefaultName(MachineRole role) => SoundPrograms.Preferred(role) ?? BuiltInDefault(role);

    public static string BuiltInDefault(MachineRole role) => role switch
    {
        MachineRole.Guitar => "Acoustic",
        MachineRole.Lead => "Electric lead",
        _ => "Finger bass",
    };

    public static string NextName(MachineRole role, string? current)
    {
        var names = Names(role);
        var index = names.ToList().FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        return names[(index + 1) % names.Count];
    }

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<(int Note, int Velocity)> _commands = new();
    private readonly Resonator _bodyLow = new();
    private readonly Resonator _bodyHigh = new();
    private int _sampleRate = 44100;
    private uint _seed = 0x2468ace1;
    private long _age;
    private PluckCharacter _character = Characters[0];
    private float _cabState;
    private float _dcState;

    public PluckSynth()
    {
        for (var v = 0; v < MaxVoices; v++)
        {
            _voices[v].Buffer = new float[BufferSize];
        }
    }

    public float Volume { get; set; } = 0.3f;

    public void SetCharacter(PluckCharacter character) => _character = character;

    public void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _bodyLow.Tune(sampleRate, 105, 0.992);
        _bodyHigh.Tune(sampleRate, 215, 0.99);
    }

    public void NoteOn(int note, int velocity) => _commands.Enqueue((note, Math.Clamp(velocity, 1, 127)));

    public void NoteOff(int note) => _commands.Enqueue((note, 0));

    public void AllNotesOff() => _commands.Enqueue((-1, 0));

    public void SustainPedal(bool down)
    {
    }

    public void PitchBend(int value)
    {
    }

    public void Render(float[] destination, int offset, int frames)
    {
        while (_commands.TryDequeue(out var command))
        {
            if (command.Note < 0)
            {
                for (var v = 0; v < MaxVoices; v++)
                {
                    Release(ref _voices[v]);
                }
            }
            else if (command.Velocity > 0)
            {
                Start(command.Note, command.Velocity);
            }
            else
            {
                for (var v = 0; v < MaxVoices; v++)
                {
                    if (_voices[v].Active && _voices[v].Note == command.Note && !_voices[v].Released)
                    {
                        Release(ref _voices[v]);
                    }
                }
            }
        }

        var body = _character.Body;
        for (var v = 0; v < MaxVoices; v++)
        {
            ref var voice = ref _voices[v];
            if (!voice.Active)
            {
                continue;
            }

            for (var i = 0; i < frames; i++)
            {
                var sample = Step(ref voice);
                if (!voice.Active)
                {
                    break;
                }

                destination[offset + i] += sample * Volume;
            }
        }

        if (body > 0f)
        {
            for (var i = 0; i < frames; i++)
            {
                var x = destination[offset + i];
                destination[offset + i] = x + (body * ((_bodyLow.Process(x) * 0.6f) + (_bodyHigh.Process(x) * 0.4f)));
            }
        }

        if (_character.Drive > 0f || _character.CabHz > 0f)
        {
            var drive = _character.Drive;
            var cab = _character.CabHz > 0f ? 1f - (float)Math.Exp(-2 * Math.PI * _character.CabHz / _sampleRate) : 1f;
            for (var i = 0; i < frames; i++)
            {
                var x = destination[offset + i];
                if (drive > 0f)
                {
                    x = (float)Math.Tanh(x * drive) * 0.55f;
                }

                _cabState += cab * (x - _cabState);
                _dcState += 0.002f * (_cabState - _dcState);
                destination[offset + i] = _cabState - _dcState;
            }
        }
    }

    private static void Release(ref Voice voice)
    {
        if (voice.Active)
        {
            voice.Released = true;
        }
    }

    private float Step(ref Voice voice)
    {
        var buffer = voice.Buffer;
        var read = voice.Write - voice.Delay;
        if (read < 0)
        {
            read += BufferSize;
        }

        var index = (int)read;
        var fraction = read - index;
        var next = buffer[(index + 1) % BufferSize];
        var current = buffer[index % BufferSize];
        var sample = current + ((next - current) * fraction);

        voice.Lowpass += voice.Brightness * (sample - voice.Lowpass);
        var loopGain = voice.Released ? 0.88f : voice.Sustain;
        buffer[voice.Write] = voice.Lowpass * loopGain;
        voice.Write = (voice.Write + 1) % BufferSize;
        voice.Age++;

        if (voice.Released)
        {
            voice.Fade *= 0.9988f;
            if (voice.Fade < 0.002f)
            {
                voice.Active = false;
            }
        }
        else if (voice.Age > _sampleRate * 8)
        {
            voice.Active = false;
        }

        if (voice.ClickLeft > 0)
        {
            var noise = NextNoise();
            sample += (noise - voice.ClickPrev) * 0.5f * voice.ClickAmp * (voice.ClickLeft / (float)voice.ClickTotal);
            voice.ClickPrev = noise;
            voice.ClickLeft--;
        }

        return sample * voice.Gain * voice.Fade;
    }

    private void Start(int note, int velocity)
    {
        var slot = -1;
        var oldest = long.MaxValue;
        for (var v = 0; v < MaxVoices; v++)
        {
            if (!_voices[v].Active)
            {
                slot = v;
                break;
            }

            if (_voices[v].Start < oldest)
            {
                oldest = _voices[v].Start;
                slot = v;
            }
        }

        ref var voice = ref _voices[slot];
        var frequency = 440.0 * Math.Pow(2, (note - 69) / 12.0);
        var level = velocity / 127f;
        var brightness = Math.Clamp(_character.Brightness + (0.12f * level), 0.1f, 0.95f);
        var delay = Math.Clamp((_sampleRate / frequency) - ((1 - brightness) / brightness), 8, BufferSize - 4);
        var length = (int)Math.Ceiling(delay) + 1;
        var buffer = voice.Buffer;
        Array.Clear(buffer);
        var smooth = 0f;
        var hammer = velocity <= MelodyGenerator.HammerVelocity;
        if (hammer)
        {
            for (var v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Active && !_voices[v].Released && _voices[v].Start == _age)
                {
                    _voices[v].Released = true;
                }
            }
        }

        var excite = Math.Clamp(_character.Excite + (0.4f * level), 0.15f, 0.95f);
        for (var i = 0; i < length; i++)
        {
            smooth += excite * (NextNoise() - smooth);
            buffer[i] = smooth * (hammer ? 0.4f + level : 0.55f + (0.45f * level));
        }

        var pick = Math.Max(1, (int)(length * _character.PickPosition));
        for (var i = length - 1; i >= pick; i--)
        {
            buffer[i] -= buffer[i - pick] * 0.7f;
        }

        voice.Active = true;
        voice.Released = false;
        voice.Note = note;
        voice.Delay = (float)delay;
        voice.Write = length;
        voice.Lowpass = 0f;
        voice.Brightness = brightness;
        voice.Sustain = (float)Math.Pow(_character.Sustain, Math.Clamp(220.0 / frequency, 0.25, 6.0));
        voice.Gain = 0.9f + (0.5f * level);
        voice.Fade = 1f;
        voice.Age = 0;
        voice.ClickTotal = Math.Max(1, (int)(_sampleRate * 0.0035));
        voice.ClickLeft = _character.Click > 0f && !hammer ? voice.ClickTotal : 0;
        voice.ClickAmp = _character.Click * (0.4f + (0.6f * level));
        voice.ClickPrev = 0f;
        voice.Start = ++_age;
    }

    private float NextNoise()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return (_seed / (float)uint.MaxValue * 2f) - 1f;
    }

    private struct Voice
    {
        public float[] Buffer;
        public bool Active;
        public bool Released;
        public int Note;
        public float Delay;
        public int Write;
        public float Lowpass;
        public float Brightness;
        public float Sustain;
        public float Gain;
        public float Fade;
        public long Age;
        public long Start;
        public int ClickTotal;
        public int ClickLeft;
        public float ClickAmp;
        public float ClickPrev;
    }

    private sealed class Resonator
    {
        private double _a1;
        private double _a2;
        private double _gain;
        private double _y1;
        private double _y2;

        public void Tune(int sampleRate, double frequency, double radius)
        {
            _a1 = -2 * radius * Math.Cos(2 * Math.PI * frequency / sampleRate);
            _a2 = radius * radius;
            var w = 2 * Math.PI * frequency / sampleRate;
            _gain = (1 - radius) * Math.Sqrt(1 - (2 * radius * Math.Cos(2 * w)) + (radius * radius));
            _y1 = 0;
            _y2 = 0;
        }

        public float Process(float input)
        {
            var y = (_gain * input) - (_a1 * _y1) - (_a2 * _y2);
            _y2 = _y1;
            _y1 = y;
            return (float)y;
        }
    }
}
