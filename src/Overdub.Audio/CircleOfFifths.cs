namespace Overdub.Audio;

public sealed record FamousProgression(string Name, bool Minor, int[] Degrees, string Text);

public static class CircleOfFifths
{
    private static readonly int[] MajorRoots = [0, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10, 5];
    private static readonly string[] SharpOrder = ["F#", "C#", "G#", "D#", "A#", "E#"];
    private static readonly string[] FlatOrder = ["Bb", "Eb", "Ab", "Db", "Gb", "Cb"];
    private static readonly int[] MajorOffsets = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorOffsets = [0, 2, 3, 5, 7, 8, 10];

    private static readonly ChordQuality[] MajorQualities =
        [ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Diminished];

    private static readonly ChordQuality[] MinorQualities =
        [ChordQuality.Minor, ChordQuality.Diminished, ChordQuality.Major, ChordQuality.Minor, ChordQuality.Minor, ChordQuality.Major, ChordQuality.Major];

    public static IReadOnlyList<FamousProgression> Famous { get; } =
    [
        new("Pop", false, [1, 5, 6, 4], "This is the famous pop progression. The V steps away to tension, the vi adds a sad turn, and the IV leans back home. Hundreds of songs use it."),
        new("Doo-wop", false, [1, 6, 4, 5], "The 1950s doo-wop progression: home, a sad turn to the vi, the IV, then the V that pulls back to the start."),
        new("Three chords", false, [1, 4, 5], "The three-chord rock and country progression. These are the three main chords of the key: home, away and tension."),
        new("Jazz cadence", false, [2, 5, 1], "The jazz cadence. The ii prepares the V, and the V resolves home to the I. Jazz is full of it."),
        new("Sad pop", false, [6, 4, 1, 5], "The same four chords as the pop progression, started on the sad vi. It sounds more emotional."),
        new("Rock minor", true, [1, 7, 6, 7], "A rock and metal favourite: it rocks between the home chord and the chords just below it."),
        new("Andalusian", true, [1, 7, 6, 5], "The Andalusian cadence: a falling line of chords that sounds Spanish and dramatic."),
        new("Minor three chords", true, [1, 4, 5], "The minor three-chord progression: home, away and tension. Dark and simple."),
        new("Minor pop", true, [1, 6, 3, 7], "A minor progression with a brighter lift on the III and VII. Common in modern pop and film music."),
    ];

    public static int Count => MajorRoots.Length;

    public static bool UsesFlats(int index) => index >= 7;

    public static int MajorRoot(int index) => MajorRoots[index];

    public static int KeyRoot(int index, bool minor) => minor ? (MajorRoots[index] + 9) % 12 : MajorRoots[index];

    public static int IndexOf(int root, bool minor)
    {
        var major = minor ? (root + 3) % 12 : root;
        return Array.IndexOf(MajorRoots, major);
    }

    public static string MajorName(int index) => NoteSpelling.NameFor(MajorRoots[index], UsesFlats(index));

    public static string MinorName(int index) => NoteSpelling.NameFor((MajorRoots[index] + 9) % 12, UsesFlats(index)) + "m";

    public static string KeyName(int index, bool minor) => minor ? $"{MinorName(index)[..^1]} minor" : $"{MajorName(index)} major";

    public static string Signature(int index)
    {
        if (index == 0)
        {
            return "No sharps or flats";
        }

        var count = index <= 6 ? index : 12 - index;
        var names = (index <= 6 ? SharpOrder : FlatOrder).Take(count);
        return $"{count} {(index <= 6 ? "sharp" : "flat")}{(count == 1 ? "" : "s")} ({string.Join(", ", names)})";
    }

    public static string Neighbours(int index, bool minor)
    {
        var left = (index + 11) % 12;
        var right = (index + 1) % 12;
        string Name(int i) => minor ? MinorName(i) : MajorName(i);
        return $"{Name(left)} and {Name(right)} are its neighbours: each shares all but one note with {KeyName(index, minor)}, so moving between them sounds smooth.";
    }

    public static Chord ChordFor(int root, bool minor, int degree)
    {
        var i = degree - 1;
        return new Chord((root + (minor ? MinorOffsets : MajorOffsets)[i]) % 12, (minor ? MinorQualities : MajorQualities)[i]);
    }

    public static IReadOnlyList<Chord> Diatonic(int root, bool minor) => Enumerable.Range(1, 7).Select(d => ChordFor(root, minor, d)).ToList();

    public static string ChordLabel(Chord chord, bool flats) =>
        NoteSpelling.NameFor(chord.Root, flats) + (chord.Quality == ChordQuality.Diminished ? "°" : chord.QualitySuffix);

    public static string Explain(int root, bool minor, IReadOnlyList<int> degrees)
    {
        if (degrees.Count == 0)
        {
            return "Tap chords from the key to build a progression. Chords that sit close together on the circle usually sound smooth side by side.";
        }

        var match = Famous.FirstOrDefault(f => f.Minor == minor && f.Degrees.SequenceEqual(degrees));
        if (match is not null)
        {
            return match.Text;
        }

        var chords = degrees.Select(d => ChordFor(root, minor, d)).ToList();
        var last = Theory.Describe(chords[^1], root, minor);
        var first = Theory.Numeral(chords[0], root, minor);
        var ending = degrees[^1] == 1
            ? "It ends on home, so it sounds finished."
            : degrees[^1] == 5
                ? "It ends on the V, which feels unfinished and pulls back to the start, so it loops well."
                : $"It ends on the {last.Numeral}, which leans towards home.";
        return $"It starts on the {first} and moves through {degrees.Count} chord{(degrees.Count == 1 ? "" : "s")} of the key. {ending}";
    }
}
