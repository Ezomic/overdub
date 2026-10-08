namespace Overdub.Audio;

public enum MachineRole
{
    Guitar,
    Bass,
}

public enum ChordQuality
{
    Major,
    Minor,
    Seventh,
    MajorSeventh,
    MinorSeventh,
    Suspended,
    Diminished,
    Power,
}

public enum ChordStyle
{
    Strum,
    FolkStrum,
    Arpeggio,
    Pad,
    WholeNotes,
    RootPulse,
    RootAndFifth,
    Walking,
    Octaves,
}

public readonly record struct Chord(int Root, ChordQuality Quality)
{
    public static readonly string[] Roots = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static readonly string[] QualityNames = ["maj", "min", "7", "maj7", "m7", "sus4", "dim", "5"];

    public string Name => Roots[Root] + (Quality == ChordQuality.Major ? "" : QualityNames[(int)Quality]);

    public int[] Intervals => Quality switch
    {
        ChordQuality.Major => [0, 4, 7],
        ChordQuality.Minor => [0, 3, 7],
        ChordQuality.Seventh => [0, 4, 7, 10],
        ChordQuality.MajorSeventh => [0, 4, 7, 11],
        ChordQuality.MinorSeventh => [0, 3, 7, 10],
        ChordQuality.Suspended => [0, 5, 7],
        ChordQuality.Diminished => [0, 3, 6],
        _ => [0, 7],
    };
}

public sealed class ChordPattern
{
    public const int MaxBars = 4;

    private static readonly double[] FolkPattern = [1, 0, 1, 1, 0, 1, 1, 1];

    private readonly Chord[] _chords = new Chord[MaxBars];

    public ChordPattern(string name, MachineRole role, int bars = 4, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N")[..8];
        Name = name;
        Role = role;
        Bars = Math.Clamp(bars, 1, MaxBars);
        Style = Styles(role)[0];
        for (var i = 0; i < MaxBars; i++)
        {
            _chords[i] = new Chord(0, ChordQuality.Major);
        }
    }

    public string Id { get; }
    public string Name { get; set; }
    public MachineRole Role { get; }
    public int Bars { get; set; }
    public ChordStyle Style { get; set; }

    public Chord this[int bar]
    {
        get => _chords[bar];
        set => _chords[bar] = value;
    }

    public static IReadOnlyList<ChordStyle> Styles(MachineRole role) => role == MachineRole.Guitar
        ? [ChordStyle.Strum, ChordStyle.FolkStrum, ChordStyle.Arpeggio, ChordStyle.Pad]
        : [ChordStyle.WholeNotes, ChordStyle.RootPulse, ChordStyle.RootAndFifth, ChordStyle.Walking, ChordStyle.Octaves];

    public static string StyleName(ChordStyle style) => style switch
    {
        ChordStyle.FolkStrum => "Folk strum",
        ChordStyle.WholeNotes => "Whole notes",
        ChordStyle.RootPulse => "Root pulse",
        ChordStyle.RootAndFifth => "Root and fifth",
        _ => style.ToString(),
    };

    public void CopyFrom(ChordPattern other)
    {
        Name = other.Name;
        Bars = other.Bars;
        Style = other.Style;
        Array.Copy(other._chords, _chords, MaxBars);
    }

    public ChordPattern Clone()
    {
        var copy = new ChordPattern(Name, Role, Bars, Id);
        copy.CopyFrom(this);
        return copy;
    }

    public string Encode() => string.Join(",", _chords.Select(c => $"{c.Root}:{(int)c.Quality}"));

    public static ChordPattern Decode(string id, string name, MachineRole role, int bars, ChordStyle style, string chords)
    {
        var pattern = new ChordPattern(name, role, bars, id) { Style = style };
        var parts = chords.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Min(parts.Length, MaxBars); i++)
        {
            var pair = parts[i].Split(':');
            pattern[i] = new Chord(Math.Clamp(int.Parse(pair[0]), 0, 11), (ChordQuality)Math.Clamp(int.Parse(pair[1]), 0, 7));
        }

        return pattern;
    }

    public static ChordPattern Starter(string name, MachineRole role)
    {
        var pattern = new ChordPattern(name, role);
        pattern[0] = new Chord(0, ChordQuality.Major);
        pattern[1] = new Chord(7, ChordQuality.Major);
        pattern[2] = new Chord(9, ChordQuality.Minor);
        pattern[3] = new Chord(5, ChordQuality.Major);
        return pattern;
    }

    public void ApplyProgression(int keyRoot, bool minor, int[] degrees)
    {
        var offsets = minor ? new[] { 0, 2, 3, 5, 7, 8, 10 } : new[] { 0, 2, 4, 5, 7, 9, 11 };
        var qualities = minor
            ? new[] { ChordQuality.Minor, ChordQuality.Diminished, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major }
            : new[] { ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Diminished };
        for (var bar = 0; bar < MaxBars; bar++)
        {
            var degree = degrees[bar % degrees.Length] - 1;
            _chords[bar] = new Chord((keyRoot + offsets[degree]) % 12, qualities[degree]);
        }
    }

    public long LengthSamples(int sampleRate, double bpm, int beatsPerBar) => (long)Math.Round(Bars * beatsPerBar * Beat(sampleRate, bpm));

    public MidiClip ToClip(int sampleRate, double bpm, int beatsPerBar, long start) => new(ToEvents(sampleRate, bpm, beatsPerBar), start) { PatternId = Id };

    public MidiEvent[] ToEvents(int sampleRate, double bpm, int beatsPerBar)
    {
        var beat = Beat(sampleRate, bpm);
        var bar = beat * beatsPerBar;
        var events = new List<MidiEvent>
        {
            new(0, 0, 0, MidiKind.Sustain, 0),
            new(LengthSamples(sampleRate, bpm, beatsPerBar), 0, 0, MidiKind.Sustain, 0),
        };
        for (var b = 0; b < Bars; b++)
        {
            var chord = _chords[b];
            var start = b * bar;
            switch (Style)
            {
                case ChordStyle.Strum:
                    for (var i = 0; i < beatsPerBar; i++)
                    {
                        Strum(events, chord, start + (i * beat), beat * 0.92, i == 0 ? 105 : 88, true, sampleRate);
                    }

                    break;
                case ChordStyle.FolkStrum:
                    for (var i = 0; i < beatsPerBar * 2; i++)
                    {
                        if (FolkPattern[i % FolkPattern.Length] == 0)
                        {
                            continue;
                        }

                        var down = i % 2 == 0;
                        Strum(events, chord, start + (i * beat / 2), beat * 0.45, i == 0 ? 105 : down ? 92 : 72, down, sampleRate);
                    }

                    break;
                case ChordStyle.Arpeggio:
                    Arpeggio(events, chord, start, beat, beatsPerBar);
                    break;
                case ChordStyle.Pad:
                    foreach (var note in GuitarVoicing(chord))
                    {
                        Add(events, start, bar * 0.98, note, 80);
                    }

                    break;
                case ChordStyle.WholeNotes:
                    Add(events, start, bar * 0.97, BassRoot(chord), 100);
                    break;
                case ChordStyle.RootPulse:
                    for (var i = 0; i < beatsPerBar * 2; i++)
                    {
                        Add(events, start + (i * beat / 2), beat * 0.42, BassRoot(chord), i % 2 == 0 ? 100 : 80);
                    }

                    break;
                case ChordStyle.RootAndFifth:
                    for (var i = 0; i < beatsPerBar; i++)
                    {
                        Add(events, start + (i * beat), beat * 0.85, BassRoot(chord) + (i % 2 == 1 ? 7 : 0), i == 0 ? 105 : 90);
                    }

                    break;
                case ChordStyle.Walking:
                    var third = chord.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh or ChordQuality.Diminished ? 3 : 4;
                    int[] walk = [0, third, 7, 9];
                    for (var i = 0; i < beatsPerBar; i++)
                    {
                        Add(events, start + (i * beat), beat * 0.9, BassRoot(chord) + walk[i % walk.Length], i == 0 ? 105 : 88);
                    }

                    break;
                default:
                    for (var i = 0; i < beatsPerBar * 2; i++)
                    {
                        Add(events, start + (i * beat / 2), beat * 0.42, BassRoot(chord) + (i % 2 == 1 ? 12 : 0), i % 2 == 0 ? 100 : 82);
                    }

                    break;
            }
        }

        var total = LengthSamples(sampleRate, bpm, beatsPerBar);
        return events.Select(e => e.At > total ? e with { At = total } : e).ToArray();
    }

    private static double Beat(int sampleRate, double bpm) => sampleRate * 60.0 / bpm;

    private static int BassRoot(Chord chord) => 28 + ((chord.Root - 4 + 12) % 12);

    private static int[] GuitarVoicing(Chord chord)
    {
        var root = 40 + ((chord.Root - 4 + 12) % 12);
        var iv = chord.Intervals;
        if (iv.Length == 2)
        {
            return [root, root + 7, root + 12, root + 19];
        }

        var top = iv.Length > 3 ? iv[3] : iv[2];
        return [root, root + 7, root + 12, root + 12 + iv[1], root + 12 + top];
    }

    private static void Add(List<MidiEvent> events, double at, double length, int note, int velocity)
    {
        var start = (long)Math.Round(at);
        events.Add(new MidiEvent(start, (byte)Math.Clamp(note, 0, 127), (byte)Math.Clamp(velocity, 1, 127)));
        events.Add(new MidiEvent(start + Math.Max(1, (long)length), (byte)Math.Clamp(note, 0, 127), 0));
    }

    private static void Strum(List<MidiEvent> events, Chord chord, double at, double length, int velocity, bool down, int sampleRate)
    {
        var notes = GuitarVoicing(chord);
        var stagger = sampleRate * 0.011;
        for (var i = 0; i < notes.Length; i++)
        {
            var order = down ? i : notes.Length - 1 - i;
            Add(events, at + (order * stagger), length, notes[i], velocity - (i * 2));
        }
    }

    private static void Arpeggio(List<MidiEvent> events, Chord chord, double start, double beat, int beatsPerBar)
    {
        var notes = GuitarVoicing(chord);
        var order = new List<int>();
        for (var i = 0; i < notes.Length; i++)
        {
            order.Add(i);
        }

        for (var i = notes.Length - 2; i >= 1; i--)
        {
            order.Add(i);
        }

        for (var step = 0; step < beatsPerBar * 2; step++)
        {
            Add(events, start + (step * beat / 2), beat * 0.9, notes[order[step % order.Count]], step % 2 == 0 ? 90 : 76);
        }
    }
}
