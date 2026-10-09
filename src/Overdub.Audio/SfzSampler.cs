using System.Collections.Concurrent;
using System.Globalization;
using NAudio.Wave;

namespace Overdub.Audio;

public sealed class SfzRegion
{
    public float[] Samples { get; set; } = [];
    public int SampleRate { get; set; } = 44100;
    public string SamplePath { get; set; } = "";
    public int LoKey { get; set; }
    public int HiKey { get; set; } = 127;
    public int KeyCenter { get; set; } = 60;
    public int LoVel { get; set; } = 1;
    public int HiVel { get; set; } = 127;
    public double LoRand { get; set; }
    public double HiRand { get; set; } = 1;
    public bool Release { get; set; }
    public double ReleaseTime { get; set; } = 0.15;
    public double VolumeDb { get; set; }
    public double RtDecay { get; set; }
    public double Tune { get; set; }
    public int Transpose { get; set; }
    public int KeyTrack { get; set; } = 100;
    public int SeqLength { get; set; } = 1;
    public int SeqPosition { get; set; } = 1;
}

public sealed class SfzInstrument
{
    private static readonly ConcurrentDictionary<string, SfzInstrument> Cache = new();

    private SfzInstrument(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public List<SfzRegion> Regions { get; } = [];
    public int MinKey { get; private set; } = 127;
    public int MaxKey { get; private set; }

    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Overdub", "Instruments");

    public static string? FindPath(string? presetName) => SoundPrograms.FindPath(presetName);

    public static SfzInstrument Load(string path) => Cache.GetOrAdd(System.IO.Path.GetFullPath(path), full =>
    {
        var instrument = new SfzInstrument(full);
        instrument.Parse();
        return instrument;
    });

    private void Parse()
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        var tokens = new List<string>();
        Expand(Path, defines, tokens, 0);

        var control = new Dictionary<string, string>();
        var global = new Dictionary<string, string>();
        var master = new Dictionary<string, string>();
        var group = new Dictionary<string, string>();
        Dictionary<string, string>? target = null;
        var pending = new List<Dictionary<string, string>>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "<control>":
                    target = control;
                    break;
                case "<global>":
                    global = [];
                    master = [];
                    group = [];
                    target = global;
                    break;
                case "<master>":
                    master = [];
                    group = [];
                    target = master;
                    break;
                case "<group>":
                    group = [];
                    target = group;
                    break;
                case "<region>":
                    var region = new Dictionary<string, string>(global);
                    foreach (var (k, v) in master)
                    {
                        region[k] = v;
                    }

                    foreach (var (k, v) in group)
                    {
                        region[k] = v;
                    }

                    pending.Add(region);
                    target = region;
                    break;
                default:
                    if (token.StartsWith('<'))
                    {
                        target = null;
                    }
                    else if (token.Contains('=') && target is not null)
                    {
                        var eq = token.IndexOf('=');
                        target[token[..eq]] = token[(eq + 1)..];
                    }

                    break;
            }
        }

        var defaultPath = control.GetValueOrDefault("default_path", "");
        var playable = pending.Where(o => o.ContainsKey("sample") && Playable(o, control, honourCc: true)).ToList();
        if (playable.Count == 0)
        {
            playable = pending.Where(o => o.ContainsKey("sample") && Playable(o, control, honourCc: false)).ToList();
        }

        var samples = new Dictionary<string, (float[] Data, int Rate)>(StringComparer.OrdinalIgnoreCase);
        foreach (var opcodes in playable)
        {
            var file = ResolveSample(directory, defaultPath, opcodes["sample"]);
            if (file is null)
            {
                continue;
            }

            if (!samples.TryGetValue(file, out var data))
            {
                data = ReadSample(file);
                samples[file] = data;
            }

            var trigger = opcodes.GetValueOrDefault("trigger", "attack");
            var r = new SfzRegion
            {
                Samples = data.Data,
                SampleRate = data.Rate,
                SamplePath = file,
                LoKey = Key(opcodes, "lokey", Key(opcodes, "key", 0)),
                HiKey = Key(opcodes, "hikey", Key(opcodes, "key", 127)),
                KeyCenter = Key(opcodes, "pitch_keycenter", Key(opcodes, "key", 60)),
                LoVel = Int(opcodes, "lovel", 1),
                HiVel = Int(opcodes, "hivel", 127),
                LoRand = Dbl(opcodes, "lorand", 0),
                HiRand = Dbl(opcodes, "hirand", 1),
                Release = trigger is "release" or "release_key",
                ReleaseTime = Dbl(opcodes, "ampeg_release", 0.15),
                VolumeDb = Dbl(opcodes, "volume", 0),
                RtDecay = Dbl(opcodes, "rt_decay", 0),
                Tune = Dbl(opcodes, "tune", 0),
                Transpose = Int(opcodes, "transpose", 0),
                KeyTrack = Int(opcodes, "pitch_keytrack", 100),
                SeqLength = Int(opcodes, "seq_length", 1),
                SeqPosition = Int(opcodes, "seq_position", 1),
            };
            Regions.Add(r);
            if (!r.Release)
            {
                MinKey = Math.Min(MinKey, r.LoKey);
                MaxKey = Math.Max(MaxKey, r.HiKey);
            }
        }
    }

    public static string? ResolveSample(string directory, string defaultPath, string sample)
    {
        var relative = sample.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var prefixed = defaultPath.Contains('$') ? relative : defaultPath.Replace('/', System.IO.Path.DirectorySeparatorChar) + relative;
        var anchor = directory;
        for (var level = 0; level < 4 && anchor is not null; level++)
        {
            foreach (var candidate in new[] { prefixed, relative, System.IO.Path.Combine("Samples", relative), System.IO.Path.Combine("samples", relative) })
            {
                var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(anchor, candidate));
                if (System.IO.File.Exists(file))
                {
                    return file;
                }
            }

            if (string.Equals(anchor, System.IO.Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            anchor = System.IO.Path.GetDirectoryName(anchor);
        }

        return null;
    }

    private static bool Playable(Dictionary<string, string> opcodes, Dictionary<string, string> control, bool honourCc)
    {
        if (opcodes.GetValueOrDefault("trigger") == "legato")
        {
            return false;
        }

        foreach (var (key, value) in opcodes)
        {
            if (!honourCc)
            {
                break;
            }

            var low = key.StartsWith("locc", StringComparison.Ordinal);
            var high = key.StartsWith("hicc", StringComparison.Ordinal);
            if (!low && !high || !int.TryParse(key[4..], out var cc) || !int.TryParse(value, out var bound))
            {
                continue;
            }

            var current = Int(control, "set_cc" + cc, 0);
            if (low ? current < bound : current > bound)
            {
                return false;
            }
        }

        if (opcodes.TryGetValue("sw_last", out _))
        {
            var last = Key(opcodes, "sw_last", -1);
            var fallback = Key(opcodes, "sw_default", Key(opcodes, "sw_lokey", last));
            return last == fallback;
        }

        return true;
    }

    private static void Expand(string file, Dictionary<string, string> defines, List<string> tokens, int depth)
    {
        if (depth > 8 || !System.IO.File.Exists(file))
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(file)!;
        foreach (var raw in System.IO.File.ReadLines(file))
        {
            var line = raw;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#include", StringComparison.Ordinal))
            {
                var open = line.IndexOf('"');
                var close = open < 0 ? -1 : line.IndexOf('"', open + 1);
                if (close > open)
                {
                    var include = Substitute(line[(open + 1)..close], defines).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    Expand(System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, include)), defines, tokens, depth + 1);
                }

                continue;
            }

            if (line.StartsWith("#define", StringComparison.Ordinal))
            {
                var parts = line.Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parts[1].StartsWith('$'))
                {
                    defines[parts[1]] = parts[2].Trim();
                }

                continue;
            }

            foreach (var token in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                tokens.Add(Substitute(token, defines));
            }
        }
    }

    private static string Substitute(string text, Dictionary<string, string> defines)
    {
        if (!text.Contains('$') || defines.Count == 0)
        {
            return text;
        }

        foreach (var (name, value) in defines.OrderByDescending(d => d.Key.Length))
        {
            text = text.Replace(name, value, StringComparison.Ordinal);
        }

        return text;
    }

    private static readonly Dictionary<char, int> NoteOffsets = new() { ['c'] = 0, ['d'] = 2, ['e'] = 4, ['f'] = 5, ['g'] = 7, ['a'] = 9, ['b'] = 11 };

    private static int Key(Dictionary<string, string> opcodes, string key, int fallback)
    {
        if (!opcodes.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        var text = value.ToLowerInvariant();
        if (text.Length < 2 || !NoteOffsets.TryGetValue(text[0], out var offset))
        {
            return fallback;
        }

        var index = 1;
        if (text[1] == '#')
        {
            offset++;
            index++;
        }
        else if (text[1] == 'b')
        {
            offset--;
            index++;
        }

        return int.TryParse(text[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave) ? ((octave + 1) * 12) + offset : fallback;
    }

    private static (float[] Data, int Rate) ReadSample(string file)
    {
        using var reader = new AudioFileReader(file);
        var provider = (ISampleProvider)reader;
        var channels = reader.WaveFormat.Channels;
        var total = (int)(reader.Length / 4);
        var buffer = new float[total];
        var read = 0;
        while (read < total)
        {
            var n = provider.Read(buffer.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        if (channels == 1)
        {
            return (buffer[..read], reader.WaveFormat.SampleRate);
        }

        var mono = new float[read / channels];
        for (var i = 0; i < mono.Length; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                sum += buffer[(i * channels) + c];
            }

            mono[i] = sum / channels;
        }

        return (mono, reader.WaveFormat.SampleRate);
    }

    private static int Int(Dictionary<string, string> opcodes, string key, int fallback) =>
        opcodes.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static double Dbl(Dictionary<string, string> opcodes, string key, double fallback) =>
        opcodes.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}

public sealed class SfzPlayer : INoteTarget
{
    private const int MaxVoices = 32;

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<(int Note, int Velocity)> _commands = new();
    private readonly int[] _heldVelocity = new int[128];
    private readonly double[] _heldSince = new double[128];
    private readonly Random _random = new(12345);
    private int _sampleRate = 44100;
    private int _sequence;
    private double _clock;
    private SfzInstrument? _instrument;

    public float Volume { get; set; } = 0.9f;

    public void Configure(int sampleRate) => _sampleRate = sampleRate;

    public void SetInstrument(SfzInstrument? instrument) => Volatile.Write(ref _instrument, instrument);

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
        var instrument = Volatile.Read(ref _instrument);
        while (_commands.TryDequeue(out var command))
        {
            if (instrument is null)
            {
                continue;
            }

            if (command.Note < 0)
            {
                for (var v = 0; v < MaxVoices; v++)
                {
                    BeginRelease(ref _voices[v]);
                }
            }
            else if (command.Velocity > 0)
            {
                _heldVelocity[command.Note] = command.Velocity;
                _heldSince[command.Note] = _clock;
                Trigger(instrument, command.Note, command.Velocity, release: false, 0);
            }
            else
            {
                for (var v = 0; v < MaxVoices; v++)
                {
                    if (_voices[v].Active && _voices[v].Note == command.Note && !_voices[v].Releasing && !_voices[v].IsReleaseSample)
                    {
                        BeginRelease(ref _voices[v]);
                    }
                }

                if (_heldVelocity[command.Note] > 0)
                {
                    Trigger(instrument, command.Note, _heldVelocity[command.Note], release: true, _clock - _heldSince[command.Note]);
                    _heldVelocity[command.Note] = 0;
                }
            }
        }

        for (var v = 0; v < MaxVoices; v++)
        {
            ref var voice = ref _voices[v];
            if (!voice.Active)
            {
                continue;
            }

            Mix(ref voice, destination, offset, frames);
        }

        _clock += frames / (double)_sampleRate;
    }

    private static void BeginRelease(ref Voice voice)
    {
        if (voice.Active && !voice.IsReleaseSample)
        {
            voice.Releasing = true;
        }
    }

    private void Trigger(SfzInstrument instrument, int note, int velocity, bool release, double held)
    {
        var roll = _random.NextDouble();
        var lookup = release ? note : Math.Clamp(note, instrument.MinKey, Math.Max(instrument.MinKey, instrument.MaxKey));
        var matches = instrument.Regions.Where(region => region.Release == release && lookup >= region.LoKey && lookup <= region.HiKey && velocity >= region.LoVel && velocity <= region.HiVel && roll >= region.LoRand && roll < region.HiRand && region.Samples.Length > 0).ToList();
        if (matches.Any(m => m.SeqLength > 1))
        {
            matches = matches.Where(m => m.SeqLength <= 1 || m.SeqPosition == (_sequence % m.SeqLength) + 1).ToList();
            _sequence++;
        }

        var layerGain = 1f / (float)Math.Sqrt(Math.Max(1, matches.Count));
        foreach (var region in matches)
        {
            var slot = -1;
            var oldest = double.MaxValue;
            for (var v = 0; v < MaxVoices; v++)
            {
                if (!_voices[v].Active)
                {
                    slot = v;
                    break;
                }

                if (_voices[v].Started < oldest)
                {
                    oldest = _voices[v].Started;
                    slot = v;
                }
            }

            var gain = layerGain * (velocity / 127f) * (float)Math.Pow(10, region.VolumeDb / 20);
            if (release && region.RtDecay > 0)
            {
                gain *= (float)Math.Pow(10, -region.RtDecay * held / 20);
            }

            var ratio = release ? 1.0 : Math.Pow(2, ((((note - region.KeyCenter) * region.KeyTrack / 100.0) + region.Transpose) / 12.0) + (region.Tune / 1200.0));
            _voices[slot] = new Voice
            {
                Active = true,
                Region = region,
                Note = note,
                Step = ratio * region.SampleRate / _sampleRate,
                Gain = gain,
                IsReleaseSample = release,
                Started = _clock,
                ReleaseStep = 1f / Math.Max(1f, (float)(region.ReleaseTime * _sampleRate)),
                Fade = 1f,
            };
        }
    }

    private void Mix(ref Voice voice, float[] destination, int offset, int frames)
    {
        var samples = voice.Region!.Samples;
        for (var i = 0; i < frames; i++)
        {
            var index = (int)voice.Position;
            if (index + 1 >= samples.Length)
            {
                voice.Active = false;
                return;
            }

            if (voice.Releasing)
            {
                voice.Fade -= voice.ReleaseStep;
                if (voice.Fade <= 0f)
                {
                    voice.Active = false;
                    return;
                }
            }

            var fraction = (float)(voice.Position - index);
            var sample = samples[index] + ((samples[index + 1] - samples[index]) * fraction);
            var attack = voice.Age < 64 ? voice.Age / 64f : 1f;
            destination[offset + i] += sample * voice.Gain * voice.Fade * attack * Volume;
            voice.Position += voice.Step;
            voice.Age++;
        }
    }

    private struct Voice
    {
        public bool Active;
        public SfzRegion? Region;
        public int Note;
        public double Position;
        public double Step;
        public float Gain;
        public bool Releasing;
        public bool IsReleaseSample;
        public double Started;
        public float ReleaseStep;
        public float Fade;
        public int Age;
    }
}
