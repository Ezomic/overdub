namespace Overdub.Audio;

public enum EarKind
{
    Intervals,
    MajorMinor,
    ChordType,
    Scale,
    BassNote,
}

public enum EarLevel
{
    Easy,
    Medium,
    Hard,
}

public sealed record EarQuestion(
    EarKind Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int Correct,
    string Tip,
    IReadOnlyList<(int Pitch, int AtMs, int LenMs)> Play,
    IReadOnlyList<(int Pitch, int AtMs, int LenMs)> Reveal,
    int RootPitch,
    IReadOnlyList<ScaleNote> Neck);

public static class EarTraining
{
    public const int ReferencePitch = 36;

    private static readonly (int Semitones, string Name, string Tip)[] Intervals =
    [
        (1, "Minor 2nd", "A minor 2nd is 1 fret, the tightest step. It sounds tense, like the Jaws theme."),
        (2, "Major 2nd", "A major 2nd is 2 frets, a gentle step up, like the first two notes of Happy Birthday."),
        (3, "Minor 3rd", "A minor 3rd is 3 frets and sounds sad and dark, like the start of Smoke on the Water."),
        (4, "Major 3rd", "A major 3rd is 4 frets and sounds bright and happy, like When the Saints Go Marching In."),
        (5, "Perfect 4th", "A perfect 4th is 5 frets and sounds like a call, like Here Comes the Bride."),
        (6, "Tritone", "A tritone is 6 frets. It sounds restless and tense, like the Simpsons theme."),
        (7, "Perfect 5th", "A perfect 5th is 7 frets and sounds open and strong, like the first two notes of the Star Wars theme."),
        (8, "Minor 6th", "A minor 6th is 8 frets and sounds wistful and a little dramatic."),
        (9, "Major 6th", "A major 6th is 9 frets and sounds warm and open, like My Bonnie Lies Over the Ocean."),
        (10, "Minor 7th", "A minor 7th is 10 frets and sounds bluesy and unfinished, like the start of Somewhere from West Side Story."),
        (11, "Major 7th", "A major 7th is 11 frets and sounds dreamy and tense, as if it wants to climb up to the octave."),
        (12, "Octave", "An octave is 12 frets: the same note, higher, like the start of Somewhere Over the Rainbow."),
    ];

    private static readonly (ChordQuality Quality, string Name, string Tip)[] Chords =
    [
        (ChordQuality.Major, "Major", "Major chords sound bright and settled."),
        (ChordQuality.Minor, "Minor", "Minor chords sound darker and sadder, because the 3rd is a half step lower."),
        (ChordQuality.Seventh, "Dominant 7th", "A dominant 7th sounds bluesy and unfinished, as if it wants to move on."),
        (ChordQuality.Suspended, "Suspended 4th", "A sus4 hangs in the air because the 3rd is replaced by the 4th. It wants to resolve."),
        (ChordQuality.MajorSeventh, "Major 7th", "A major 7th sounds dreamy and smooth."),
        (ChordQuality.MinorSeventh, "Minor 7th", "A minor 7th sounds mellow and jazzy."),
        (ChordQuality.Diminished, "Diminished", "A diminished chord sounds tense and unstable, with two minor 3rds stacked."),
        (ChordQuality.Power, "Power chord", "A power chord is only the root and the 5th: no 3rd, so it is neither major nor minor."),
    ];

    private static readonly int[] EasyIntervals = [3, 5, 7, 12];
    private static readonly int[] MediumIntervals = [2, 3, 4, 5, 7, 9, 10, 12];
    private static readonly int[] EasyChords = [0, 1, 2, 3];
    private static readonly int[] MediumChords = [0, 1, 2, 3, 4, 5];
    private static readonly string[] EasyScales = ["Major", "Natural minor", "Minor pentatonic", "Blues"];
    private static readonly string[] MediumScales = ["Major", "Natural minor", "Minor pentatonic", "Blues", "Dorian", "Mixolydian"];

    public static string KindName(EarKind kind) => kind switch
    {
        EarKind.Intervals => "Intervals",
        EarKind.MajorMinor => "Major or minor",
        EarKind.ChordType => "Chord type",
        EarKind.Scale => "Scale or mode",
        _ => "Name the bass note",
    };

    public static IReadOnlyList<EarQuestion> Build(EarKind kind, EarLevel level, int count, Random rng)
    {
        var questions = new List<EarQuestion>();
        var seen = new HashSet<string>();
        var attempts = 0;
        while (questions.Count < count && attempts++ < count * 30)
        {
            var question = Generate(kind, level, rng);
            if (seen.Add(question.Prompt + "|" + question.Options[question.Correct] + "|" + string.Join(",", question.Play.Select(n => n.Pitch))))
            {
                questions.Add(question);
            }
        }

        return questions;
    }

    private static EarQuestion Generate(EarKind kind, EarLevel level, Random rng) => kind switch
    {
        EarKind.Intervals => Interval(level, rng),
        EarKind.MajorMinor => MajorMinor(level, rng),
        EarKind.ChordType => ChordType(level, rng),
        EarKind.Scale => Scale(level, rng),
        _ => BassNote(level, rng),
    };

    private static List<string> Options(string correct, IEnumerable<string> pool, Random rng, int count = 4)
    {
        var options = new List<string> { correct };
        options.AddRange(pool.Where(p => p != correct).OrderBy(_ => rng.Next()).Take(count - 1));
        return options.OrderBy(_ => rng.Next()).ToList();
    }

    private static ScaleNote Marker(int pitch, ScaleRole role, bool flats, string label) =>
        new(pitch % 12, 0, NoteSpelling.NameFor(pitch % 12, flats), label, role);

    private static EarQuestion Interval(EarLevel level, Random rng)
    {
        var pool = level switch
        {
            EarLevel.Easy => EasyIntervals,
            EarLevel.Medium => MediumIntervals,
            _ => Enumerable.Range(1, 12).ToArray(),
        };
        var root = 36 + rng.Next(12);
        var semitonesChosen = pool[rng.Next(pool.Length)];
        var chosen = Intervals.First(i => i.Semitones == semitonesChosen);
        var names = pool.Select(p => Intervals.First(i => i.Semitones == p).Name);
        var options = Options(chosen.Name, names, rng);
        var flats = NoteSpelling.KeyUsesFlats(root % 12, false);
        return new EarQuestion(
            EarKind.Intervals,
            "How far apart are the two notes?",
            options,
            options.IndexOf(chosen.Name),
            chosen.Tip,
            [(root, 0, 800), (root + chosen.Semitones, 900, 1000)],
            [(root, 0, 800), (root + chosen.Semitones, 900, 800), (root, 2200, 1400), (root + chosen.Semitones, 2200, 1400)],
            root,
            [Marker(root, ScaleRole.Root, flats, "1"), Marker(root + chosen.Semitones, ScaleRole.ChordTone, flats, chosen.Semitones.ToString())]);
    }

    private static List<(int Pitch, int AtMs, int LenMs)> ChordTogether(int root, ChordQuality quality) =>
        new Chord(0, quality).Intervals.Select(i => (root + i, 0, 1600)).ToList();

    private static List<(int Pitch, int AtMs, int LenMs)> ChordArpeggio(int root, ChordQuality quality) =>
        new Chord(0, quality).Intervals.Select((i, n) => (root + i, n * 350, 500)).ToList();

    private static IReadOnlyList<ScaleNote> ChordNeck(int root, ChordQuality quality)
    {
        var flats = NoteSpelling.KeyUsesFlats(root % 12, false);
        return new Chord(0, quality).Intervals.Select((i, n) => Marker(root + i, n == 0 ? ScaleRole.Root : ScaleRole.ChordTone, flats, i.ToString())).ToList();
    }

    private static EarQuestion MajorMinor(EarLevel level, Random rng)
    {
        var minor = rng.Next(2) == 0;
        var root = 36 + rng.Next(12);
        var quality = level == EarLevel.Hard ? (minor ? ChordQuality.MinorSeventh : ChordQuality.MajorSeventh) : (minor ? ChordQuality.Minor : ChordQuality.Major);
        var arpeggio = level == EarLevel.Medium;
        var answer = minor ? "Minor" : "Major";
        return new EarQuestion(
            EarKind.MajorMinor,
            level == EarLevel.Hard ? "Does this chord sound major or minor?" : "Is this chord major or minor?",
            ["Major", "Minor"],
            minor ? 1 : 0,
            minor ? "Minor sounds darker and sadder, because its 3rd is a half step lower than in a major chord." : "Major sounds brighter and happier, because its 3rd is a half step higher than in a minor chord.",
            arpeggio ? ChordArpeggio(root, quality) : ChordTogether(root, quality),
            ChordArpeggio(root, quality).Concat(ChordTogether(root, quality).Select(n => (n.Pitch, 1800, 1600))).ToList(),
            root,
            ChordNeck(root, quality));
    }

    private static EarQuestion ChordType(EarLevel level, Random rng)
    {
        var pool = level switch
        {
            EarLevel.Easy => EasyChords,
            EarLevel.Medium => MediumChords,
            _ => Enumerable.Range(0, Chords.Length).ToArray(),
        };
        var chosen = Chords[pool[rng.Next(pool.Length)]];
        var root = 36 + rng.Next(12);
        var options = Options(chosen.Name, pool.Select(i => Chords[i].Name), rng);
        return new EarQuestion(
            EarKind.ChordType,
            "What kind of chord is this?",
            options,
            options.IndexOf(chosen.Name),
            chosen.Tip,
            ChordTogether(root, chosen.Quality),
            ChordArpeggio(root, chosen.Quality).Concat(ChordTogether(root, chosen.Quality).Select(n => (n.Pitch, 1800, 1600))).ToList(),
            root,
            ChordNeck(root, chosen.Quality));
    }

    private static EarQuestion Scale(EarLevel level, Random rng)
    {
        var names = level switch
        {
            EarLevel.Easy => EasyScales,
            EarLevel.Medium => MediumScales,
            _ => ScaleExplorer.All.Select(s => s.Name).ToArray(),
        };
        var chosen = ScaleExplorer.Find(names[rng.Next(names.Length)]);
        var root = rng.Next(12);
        var options = Options(chosen.Name, names, rng);
        var melody = ScaleExplorer.Melody(chosen, root, 360);
        var tip = ScaleExplorer.Find(chosen.Name).Sound;
        return new EarQuestion(
            EarKind.Scale,
            "Which scale is this?",
            options,
            options.IndexOf(chosen.Name),
            tip,
            melody,
            melody,
            36 + root,
            ScaleExplorer.Notes(chosen, root));
    }

    private static EarQuestion BassNote(EarLevel level, Random rng)
    {
        var semitones = level switch
        {
            EarLevel.Easy => new[] { 0, 2, 4, 5, 7, 9, 11 }[rng.Next(7)],
            _ => rng.Next(12),
        };
        var pool = level == EarLevel.Easy ? new[] { 0, 2, 4, 5, 7, 9, 11 } : Enumerable.Range(0, 12).ToArray();
        var pitch = ReferencePitch + semitones;
        var name = NoteSpelling.NameFor(pitch % 12, false);
        var options = Options(name, pool.Select(p => NoteSpelling.NameFor(p, false)), rng);
        var flats = false;
        return new EarQuestion(
            EarKind.BassNote,
            "Which note is it?",
            options,
            options.IndexOf(name),
            semitones == 0
                ? "The note was C, the same as the reference. Find it on your bass and play it back to lock in the sound."
                : $"The note was {name}, {semitones} fret{(semitones == 1 ? "" : "s")} above the C. Find it on your bass and play it back to lock in the sound.",
            [(pitch, 0, 1400)],
            [(ReferencePitch, 0, 800), (pitch, 900, 1400)],
            ReferencePitch,
            [Marker(ReferencePitch, ScaleRole.Root, flats, "C"), Marker(pitch, ScaleRole.ChordTone, flats, name)]);
    }
}
