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

    public static IReadOnlyList<(string Name, string Path)> Discover()
    {
        if (!System.IO.Directory.Exists(Folder))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Folder, "*.sfz", SearchOption.AllDirectories)
            .Select(file =>
            {
                var folder = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(file)!).Name;
                var name = System.IO.Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
                return ($"{folder}: {name} (sampled)", file);
            })
            .OrderBy(x => x.Item1)
            .ToList();
    }

    public static string? FindPath(string? presetName) => presetName is null ? null : Discover().FirstOrDefault(d => d.Name == presetName).Path;

    public static SfzInstrument Load(string path) => Cache.GetOrAdd(System.IO.Path.GetFullPath(path), full =>
    {
        var instrument = new SfzInstrument(full);
        instrument.Parse();
        return instrument;
    });

    private void Parse()
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var group = new Dictionary<string, string>();
        Dictionary<string, string>? region = null;
        var samples = new Dictionary<string, (float[] Data, int Rate)>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<Dictionary<string, string>>();

        foreach (var raw in System.IO.File.ReadLines(Path))
        {
            var line = raw;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            foreach (var token in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("<group>", StringComparison.Ordinal))
                {
                    group = [];
                    region = null;
                }
                else if (token.StartsWith("<region>", StringComparison.Ordinal))
                {
                    region = new Dictionary<string, string>(group);
                    pending.Add(region);
                }
                else if (token.StartsWith('<'))
                {
                    region = null;
                    group = [];
                }
                else if (token.Contains('='))
                {
                    var eq = token.IndexOf('=');
                    var key = token[..eq];
                    var value = token[(eq + 1)..];
                    (region ?? group)[key] = value;
                }
            }
        }

        foreach (var opcodes in pending)
        {
            if (!opcodes.TryGetValue("sample", out var sample))
            {
                continue;
            }

            var file = System.IO.Path.Combine(directory, sample.Replace('\\', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(file))
            {
                continue;
            }

            if (!samples.TryGetValue(file, out var data))
            {
                data = ReadSample(file);
                samples[file] = data;
            }

            var r = new SfzRegion
            {
                Samples = data.Data,
                SampleRate = data.Rate,
                SamplePath = file,
                LoKey = Int(opcodes, "lokey", Int(opcodes, "key", 0)),
                HiKey = Int(opcodes, "hikey", Int(opcodes, "key", 127)),
                KeyCenter = Int(opcodes, "pitch_keycenter", Int(opcodes, "key", 60)),
                LoVel = Int(opcodes, "lovel", 1),
                HiVel = Int(opcodes, "hivel", 127),
                LoRand = Dbl(opcodes, "lorand", 0),
                HiRand = Dbl(opcodes, "hirand", 1),
                Release = opcodes.TryGetValue("trigger", out var trigger) && trigger == "release",
                ReleaseTime = Dbl(opcodes, "ampeg_release", 0.15),
                VolumeDb = Dbl(opcodes, "volume", 0),
                RtDecay = Dbl(opcodes, "rt_decay", 0),
                Tune = Dbl(opcodes, "tune", 0),
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

            var ratio = release ? 1.0 : Math.Pow(2, ((note - region.KeyCenter) / 12.0) + (region.Tune / 1200.0));
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
