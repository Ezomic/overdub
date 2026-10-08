namespace Overdub.Audio;

public sealed class DrumPattern
{
    public const int StepsPerBar = 16;
    public const int MaxBars = 2;

    private byte[][] _steps;

    public DrumPattern(string name, int bars = 1, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N")[..8];
        Name = name;
        Bars = Math.Clamp(bars, 1, MaxBars);
        _steps = NewSteps(Bars);
    }

    public string Id { get; }
    public string Name { get; set; }
    public int Bars { get; private set; }
    public int Steps => Bars * StepsPerBar;

    public byte Get(int lane, int step) => _steps[lane][step];

    public void Set(int lane, int step, byte level) => _steps[lane][step] = Math.Clamp(level, (byte)0, (byte)2);

    public void SetBars(int bars)
    {
        bars = Math.Clamp(bars, 1, MaxBars);
        var resized = NewSteps(bars);
        for (var lane = 0; lane < resized.Length; lane++)
        {
            Array.Copy(_steps[lane], resized[lane], Math.Min(_steps[lane].Length, resized[lane].Length));
            if (bars > Bars)
            {
                for (var step = Bars * StepsPerBar; step < bars * StepsPerBar; step++)
                {
                    resized[lane][step] = _steps[lane][step % (Bars * StepsPerBar)];
                }
            }
        }

        Bars = bars;
        _steps = resized;
    }

    public void CopyFrom(DrumPattern other)
    {
        Name = other.Name;
        Bars = other.Bars;
        _steps = other._steps.Select(row => (byte[])row.Clone()).ToArray();
    }

    public DrumPattern Clone()
    {
        var copy = new DrumPattern(Name, Bars, Id);
        copy.CopyFrom(this);
        return copy;
    }

    public bool IsEmpty => _steps.All(row => row.All(level => level == 0));

    public void Clear()
    {
        foreach (var row in _steps)
        {
            Array.Clear(row);
        }
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

    public MidiEvent[] ToEvents(int sampleRate, double bpm)
    {
        var step = StepSamples(sampleRate, bpm);
        var events = new List<MidiEvent>
        {
            new(0, 0, 0, MidiKind.Sustain, 0),
            new(LengthSamples(sampleRate, bpm), 0, 0, MidiKind.Sustain, 0),
        };
        for (var lane = 0; lane < DrumKit.Lanes.Count; lane++)
        {
            for (var s = 0; s < Steps; s++)
            {
                var level = _steps[lane][s];
                if (level == 0)
                {
                    continue;
                }

                var at = (long)Math.Round(s * step);
                events.Add(new MidiEvent(at, DrumKit.Lanes[lane].Note, level == 2 ? (byte)127 : (byte)90));
                events.Add(new MidiEvent(at + (long)(step / 2), DrumKit.Lanes[lane].Note, 0));
            }
        }

        return events.ToArray();
    }

    public MidiClip ToClip(int sampleRate, double bpm, long start) => new(ToEvents(sampleRate, bpm), start) { PatternId = Id };

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

    private static double StepSamples(int sampleRate, double bpm) => sampleRate * 60.0 / bpm / 4.0;

    private static byte[][] NewSteps(int bars) => Enumerable.Range(0, DrumKit.Lanes.Count).Select(_ => new byte[bars * StepsPerBar]).ToArray();
}
