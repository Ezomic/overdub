namespace Overdub.Audio;

public enum MachineRole
{
    Guitar,
    Bass,
    Lead,
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
    Rock8ths,
    Syncopated,
    Funk,
    Motown,
    Reggae,
    WalkingApproach,
}

public readonly record struct Chord(int Root, ChordQuality Quality)
{
    public static readonly string[] Roots = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static readonly string[] QualityNames = ["maj", "min", "7", "maj7", "m7", "sus4", "dim", "5"];

    public string Name => Roots[Root] + (Quality switch { ChordQuality.Major => "", ChordQuality.Minor => "m", _ => QualityNames[(int)Quality] });

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
    public string? FollowId { get; set; }
    public int Feel { get; set; }
    public int Articulation { get; set; }

    public static readonly string[] ArticulationNames = ["Open", "Palm mute"];

    public Chord this[int bar]
    {
        get => _chords[bar];
        set => _chords[bar] = value;
    }

    public static IReadOnlyList<ChordStyle> Styles(MachineRole role) => role == MachineRole.Guitar
        ? [ChordStyle.Strum, ChordStyle.FolkStrum, ChordStyle.Arpeggio, ChordStyle.Pad]
        : BassStyles;

    public static readonly IReadOnlyList<ChordStyle> BassStyles =
    [
        ChordStyle.WholeNotes, ChordStyle.RootPulse, ChordStyle.RootAndFifth, ChordStyle.Octaves, ChordStyle.Rock8ths,
        ChordStyle.Syncopated, ChordStyle.Motown, ChordStyle.Funk, ChordStyle.Reggae, ChordStyle.Walking, ChordStyle.WalkingApproach,
    ];

    public static string StyleDescription(ChordStyle style) => style switch
    {
        ChordStyle.WholeNotes => "One long root note per bar. Locks with the kick and leaves room for the other instruments.",
        ChordStyle.RootPulse => "The root note on every eighth. Drives the song and is a good first thing to get steady.",
        ChordStyle.RootAndFifth => "Root on beats 1 and 3, the fifth on beats 2 and 4. The fifth outlines the chord without changing its sound.",
        ChordStyle.Octaves => "The root jumping up an octave every other eighth, the classic disco and pop pump.",
        ChordStyle.Rock8ths => "Steady eighth-note roots with the fifth on the last eighth to push into the next beat.",
        ChordStyle.Syncopated => "A 3 + 3 + 2 grouping of the beat. The accents land between the beats, which gives it forward lean.",
        ChordStyle.Motown => "Eighth notes with octave jumps and the fifth. Busy but always moving through chord tones.",
        ChordStyle.Funk => "Sixteenth-note feel with muted ghost notes (the x marks) between the main notes. Keep the ghost notes quiet and short.",
        ChordStyle.Reggae => "Beat 1 left empty with long notes after it. The space is the style, so do not fill it.",
        ChordStyle.Walking => "One note per beat: root, third, fifth, sixth. Walks up the chord and works at slow and medium tempos.",
        ChordStyle.WalkingApproach => "Like walking, but the last beat steps up a half step into the next chord's root. This is how bass lines lead into a chord change.",
        _ => "",
    };

    public static string StyleName(ChordStyle style) => style switch
    {
        ChordStyle.Rock8ths => "Rock eighths",
        ChordStyle.WalkingApproach => "Walking with approach note",
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
        FollowId = other.FollowId;
        Feel = other.Feel;
        Articulation = other.Articulation;
        Array.Copy(other._chords, _chords, MaxBars);
    }

    public ChordPattern WithChordsFrom(ChordPattern source)
    {
        var copy = Clone();
        copy.Bars = source.Bars;
        Array.Copy(source._chords, copy._chords, MaxBars);
        return copy;
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

    public MidiClip ToClip(int sampleRate, double bpm, int beatsPerBar, long start) => new(ToEvents(sampleRate, bpm, beatsPerBar, Humanizer.StableSeed(Id, start)), start) { PatternId = Id };

    public MidiEvent[] ToEvents(int sampleRate, double bpm, int beatsPerBar, int seed = 0)
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
                        Strum(events, chord, start + (i * beat), beat * 0.92, i == 0 ? 105 : i % 2 == 0 ? 88 : 76, Feel == 0 || i % 2 == 0, sampleRate);
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
                default:
                    BassBar(events, Style, chord, _chords[(b + 1) % Bars], start, beat, beatsPerBar);
                    break;
            }
        }

        var total = LengthSamples(sampleRate, bpm, beatsPerBar);
        if (Articulation == 1 && Role == MachineRole.Guitar)
        {
            for (var i = 2; i + 1 < events.Count; i += 2)
            {
                var on = events[i];
                var limit = on.At + (long)(beat * 0.2);
                events[i] = on with { Velocity = (byte)Math.Max(1, on.Velocity - 10) };
                events[i + 1] = events[i + 1] with { At = Math.Min(events[i + 1].At, limit) };
            }
        }

        var clamped = events.Select(e => e.At > total ? e with { At = total } : e).ToList();
        return seed != 0 && Feel > 0 ? Humanizer.Apply(clamped, Feel, seed, sampleRate, total) : clamped.ToArray();
    }

    private static readonly Dictionary<ChordStyle, (int Step, char Token, int Length)[]> BassGrid = new()
    {
        [ChordStyle.WholeNotes] = [(0, 'R', 16)],
        [ChordStyle.RootPulse] = [(0, 'R', 2), (2, 'R', 2), (4, 'R', 2), (6, 'R', 2), (8, 'R', 2), (10, 'R', 2), (12, 'R', 2), (14, 'R', 2)],
        [ChordStyle.RootAndFifth] = [(0, 'R', 4), (4, '5', 4), (8, 'R', 4), (12, '5', 4)],
        [ChordStyle.Octaves] = [(0, 'R', 2), (2, 'O', 2), (4, 'R', 2), (6, 'O', 2), (8, 'R', 2), (10, 'O', 2), (12, 'R', 2), (14, 'O', 2)],
        [ChordStyle.Rock8ths] = [(0, 'R', 2), (2, 'R', 2), (4, 'R', 2), (6, 'R', 2), (8, 'R', 2), (10, 'R', 2), (12, 'R', 2), (14, '5', 2)],
        [ChordStyle.Syncopated] = [(0, 'R', 3), (3, 'R', 3), (6, 'R', 2), (8, 'R', 3), (11, 'R', 3), (14, '5', 2)],
        [ChordStyle.Motown] = [(0, 'R', 2), (2, 'R', 2), (4, 'O', 2), (6, '5', 2), (8, 'R', 2), (10, 'R', 2), (12, 'O', 2), (14, '5', 2)],
        [ChordStyle.Funk] = [(0, 'R', 3), (3, 'G', 1), (6, 'O', 2), (8, 'R', 3), (11, 'G', 1), (12, '5', 2), (14, 'G', 1)],
        [ChordStyle.Reggae] = [(4, 'R', 3), (10, 'R', 3), (14, '5', 2)],
        [ChordStyle.Walking] = [(0, 'R', 4), (4, '3', 4), (8, '5', 4), (12, '6', 4)],
        [ChordStyle.WalkingApproach] = [(0, 'R', 4), (4, '3', 4), (8, '5', 4), (12, 'A', 4)],
    };

    private static void BassBar(List<MidiEvent> events, ChordStyle style, Chord chord, Chord next, double start, double beat, int beatsPerBar)
    {
        var sixteenth = beat / 4;
        var steps = beatsPerBar * 4;
        var root = BassRoot(chord);
        var third = chord.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh or ChordQuality.Diminished ? 3 : 4;
        foreach (var (step, token, length) in BassGrid[style])
        {
            if (step >= steps)
            {
                continue;
            }

            var ghost = token == 'G';
            var pitch = token switch
            {
                '5' => root + 7,
                'O' => root + 12,
                '3' => root + third,
                '6' => root + 9,
                'A' => ApproachNote(BassRoot(next)),
                _ => root,
            };
            var velocity = ghost ? 40 : step % 4 == 0 ? 105 : step % 2 == 0 ? 90 : 78;
            var span = Math.Min(length, steps - step) * sixteenth * (ghost ? 0.45 : 0.9);
            Add(events, start + (step * sixteenth), span, pitch, velocity);
        }
    }

    private static int ApproachNote(int nextRoot) => nextRoot - 1 >= 28 ? nextRoot - 1 : nextRoot + 1;

    public static int BassRootNote(Chord chord) => BassRoot(chord);

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
