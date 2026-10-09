namespace Overdub.Audio;

public sealed class DrumPattern
{
    public const int StepsPerBar = 16;
    public const int MaxBars = 2;

    private byte[][] _steps;
    private byte[][] _velocity;
    private byte[][] _chance;

    public DrumPattern(string name, int bars = 1, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N")[..8];
        Name = name;
        Bars = Math.Clamp(bars, 1, MaxBars);
        _steps = NewSteps(Bars);
        _velocity = NewSteps(Bars);
        _chance = NewSteps(Bars);
    }

    public string Id { get; }
    public string Name { get; set; }
    public int Bars { get; private set; }
    public int Steps => Bars * StepsPerBar;
    public int Feel { get; set; }
    public int Swing { get; set; }

    public static readonly string[] SwingNames = ["Straight", "Light", "Medium", "Heavy"];
    private static readonly double[] SwingAmounts = [0, 0.4, 0.7, 1.0];

    public byte Get(int lane, int step) => _steps[lane][step];

    public void Set(int lane, int step, byte level)
    {
        level = Math.Clamp(level, (byte)0, (byte)2);
        if (level != _steps[lane][step])
        {
            _velocity[lane][step] = 0;
        }

        if (level == 0)
        {
            _chance[lane][step] = 0;
        }

        _steps[lane][step] = level;
    }

    public int Velocity(int lane, int step) => _velocity[lane][step] != 0 ? _velocity[lane][step] : _steps[lane][step] == 2 ? 127 : 90;

    public int Chance(int lane, int step) => _chance[lane][step] == 0 ? 100 : _chance[lane][step];

    public void SetVelocity(int lane, int step, int velocity)
    {
        if (_steps[lane][step] != 0)
        {
            _velocity[lane][step] = (byte)Math.Clamp(velocity, 1, 127);
        }
    }

    public void SetChance(int lane, int step, int chance)
    {
        if (_steps[lane][step] != 0)
        {
            _chance[lane][step] = chance >= 100 ? (byte)0 : (byte)Math.Clamp(chance, 1, 99);
        }
    }

    public IReadOnlyList<string> Details()
    {
        var list = new List<string>();
        for (var lane = 0; lane < _steps.Length; lane++)
        {
            for (var step = 0; step < Steps; step++)
            {
                if (_velocity[lane][step] != 0 || _chance[lane][step] != 0)
                {
                    list.Add($"{lane},{step},{_velocity[lane][step]},{_chance[lane][step]}");
                }
            }
        }

        return list;
    }

    public void ApplyDetails(IEnumerable<string>? details)
    {
        foreach (var entry in details ?? [])
        {
            var parts = entry.Split(',');
            if (parts.Length == 4 && int.TryParse(parts[0], out var lane) && int.TryParse(parts[1], out var step) && lane >= 0 && lane < _steps.Length && step >= 0 && step < Steps)
            {
                _velocity[lane][step] = byte.Parse(parts[2]);
                _chance[lane][step] = byte.Parse(parts[3]);
            }
        }
    }

    private void ClearDetails()
    {
        foreach (var row in _velocity.Concat(_chance))
        {
            Array.Clear(row);
        }
    }

    public void SetBars(int bars)
    {
        bars = Math.Clamp(bars, 1, MaxBars);
        _steps = Resized(_steps, bars);
        _velocity = Resized(_velocity, bars);
        _chance = Resized(_chance, bars);
        Bars = bars;
    }

    private byte[][] Resized(byte[][] source, int bars)
    {
        var resized = NewSteps(bars);
        for (var lane = 0; lane < resized.Length; lane++)
        {
            Array.Copy(source[lane], resized[lane], Math.Min(source[lane].Length, resized[lane].Length));
            if (bars > Bars)
            {
                for (var step = Bars * StepsPerBar; step < bars * StepsPerBar; step++)
                {
                    resized[lane][step] = source[lane][step % (Bars * StepsPerBar)];
                }
            }
        }

        return resized;
    }

    public void CopyFrom(DrumPattern other)
    {
        Name = other.Name;
        Bars = other.Bars;
        Feel = other.Feel;
        Swing = other.Swing;
        _steps = other._steps.Select(row => (byte[])row.Clone()).ToArray();
        _velocity = other._velocity.Select(row => (byte[])row.Clone()).ToArray();
        _chance = other._chance.Select(row => (byte[])row.Clone()).ToArray();
    }

    public DrumPattern Clone()
    {
        var copy = new DrumPattern(Name, Bars, Id);
        copy.CopyFrom(this);
        return copy;
    }

    public static readonly string[] FillNames = ["snare roll", "tom run", "kick and snare"];

    public DrumPattern WithFill(int type, string name)
    {
        var copy = new DrumPattern(name, Bars);
        copy.CopyFrom(this);
        copy.Name = name;
        var first = Steps - 4;
        for (var lane = 0; lane < DrumKit.Lanes.Count; lane++)
        {
            for (var step = first; step < Steps; step++)
            {
                copy.Set(lane, step, 0);
            }
        }

        (int Lane, int Offset, byte Level)[] hits = (type % FillNames.Length) switch
        {
            0 => [(1, 0, 1), (1, 1, 1), (1, 2, 1), (1, 3, 2)],
            1 => [(6, 0, 1), (6, 1, 1), (5, 2, 1), (5, 3, 2)],
            _ => [(0, 0, 2), (1, 1, 1), (0, 2, 2), (1, 3, 2)],
        };
        foreach (var (lane, offset, level) in hits)
        {
            copy.Set(lane, first + offset, level);
        }

        return copy;
    }

    public bool IsEmpty => _steps.All(row => row.All(level => level == 0));

    public void Clear()
    {
        foreach (var row in _steps)
        {
            Array.Clear(row);
        }

        ClearDetails();
    }

    public string[] Encode() => _steps.Select(row => string.Concat(row.Select(level => (char)('0' + level)))).ToArray();

    public static DrumPattern Decode(string id, string name, int bars, IReadOnlyList<string> lanes)
    {
        var pattern = new DrumPattern(name, bars, id);
        for (var lane = 0; lane < Math.Min(lanes.Count, DrumKit.Lanes.Count); lane++)
        {
            for (var step = 0; step < Math.Min(lanes[lane].Length, pattern.Steps); step++)
            {
                pattern.Set(lane, step, (byte)Math.Clamp(lanes[lane][step] - '0', 0, 2));
            }
        }

        return pattern;
    }

    public long LengthSamples(int sampleRate, double bpm) => (long)Math.Round(Steps * StepSamples(sampleRate, bpm));

    public MidiEvent[] ToEvents(int sampleRate, double bpm, int seed = 0)
    {
        var step = StepSamples(sampleRate, bpm);
        var events = new List<MidiEvent>
        {
            new(0, 0, 0, MidiKind.Sustain, 0),
            new(LengthSamples(sampleRate, bpm), 0, 0, MidiKind.Sustain, 0),
        };
        var random = seed != 0 ? new Random(seed ^ 0x5bd1) : null;
        for (var lane = 0; lane < DrumKit.Lanes.Count; lane++)
        {
            for (var s = 0; s < Steps; s++)
            {
                var level = _steps[lane][s];
                if (level == 0 || (random is not null && Chance(lane, s) < 100 && random.Next(100) >= Chance(lane, s)))
                {
                    continue;
                }

                var at = (long)Math.Round((s * step) + SwingDelay(s) * step);
                events.Add(new MidiEvent(at, DrumKit.Lanes[lane].Note, (byte)Velocity(lane, s)));
                events.Add(new MidiEvent(at + (long)(step / 2), DrumKit.Lanes[lane].Note, 0));
            }
        }

        return seed != 0 && Feel > 0 ? Humanizer.Apply(events, Feel, seed, sampleRate, LengthSamples(sampleRate, bpm)) : events.ToArray();
    }

    public MidiClip ToClip(int sampleRate, double bpm, long start) => new(ToEvents(sampleRate, bpm, Humanizer.StableSeed(Id, start)), start) { PatternId = Id };

    public static IReadOnlyList<(string Name, string[] Lanes)> Presets { get; } =
    [
        ("Rock", ["2000000010100000", "0000200000002000", "1010101010101010", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "2000000000000000"]),
        ("Pop", ["2000001020000010", "0000200000002000", "1010101010101010", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Disco", ["2000200020002000", "0000200000002000", "1000100010001000", "0010001000100010", "0000100000001000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("House", ["2000200020002000", "0000000000000000", "1010101010101010", "0010001000100010", "0000200000002000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Hip-hop", ["2000000100100000", "0000200000002000", "1010101010101011", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Funk", ["2001001000100100", "0000200100102000", "2111211121112111", "0000000000000010", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Half-time", ["2000000000100000", "0000000020000000", "1010101010101010", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Metal", ["2121212121212121", "0000200000002000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "2000000000000000"]),
        ("Reggae", ["0000000020000000", "0000000020000000", "0101010101010101", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Bossa nova", ["2001001020010010", "0000000000000000", "1010101010101010", "0000000000000000", "1001001000100100", "0000000000000000", "0000000000000000", "0000000000000000"]),
        ("Tom fill", ["2000000000000000", "0000000000001010", "0000000000000000", "0000000000000000", "0000000000000000", "0000000010100000", "0000000000001010", "0000000000000000"]),
    ];

    public void ApplyPreset(string name)
    {
        var preset = Presets.First(p => p.Name == name);
        ClearDetails();
        for (var lane = 0; lane < DrumKit.Lanes.Count; lane++)
        {
            for (var step = 0; step < Steps; step++)
            {
                Set(lane, step, (byte)(preset.Lanes[lane][step % StepsPerBar] - '0'));
            }
        }
    }

    public static DrumPattern Starter(string name)
    {
        var pattern = new DrumPattern(name);
        for (var s = 0; s < StepsPerBar; s += 2)
        {
            pattern.Set(2, s, 1);
        }

        foreach (var s in new[] { 0, 8 })
        {
            pattern.Set(0, s, 2);
        }

        foreach (var s in new[] { 4, 12 })
        {
            pattern.Set(1, s, 2);
        }

        return pattern;
    }

    private double SwingDelay(int step) => step % 4 == 2 ? SwingAmounts[Swing] * (2.0 / 3) : step % 2 == 1 ? SwingAmounts[Swing] / 3 : 0;

    private static double StepSamples(int sampleRate, double bpm) => sampleRate * 60.0 / bpm / 4.0;

    private static byte[][] NewSteps(int bars) => Enumerable.Range(0, DrumKit.Lanes.Count).Select(_ => new byte[bars * StepsPerBar]).ToArray();
}
