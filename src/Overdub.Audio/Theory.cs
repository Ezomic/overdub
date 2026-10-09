namespace Overdub.Audio;

public enum ChordFunction
{
    Home,
    Away,
    Tension,
    RelativeMinor,
    RelativeMajor,
    SoftHome,
    Borrowed,
    Colour,
    Blues,
}

public sealed record ChordInfo(string Numeral, string Name, ChordFunction Function, string FunctionLabel, string Title, string Explanation, string BassTip, int Degree);

public static class Theory
{
    private static readonly string[] Ordinals = ["", "1st", "2nd", "3rd", "4th", "5th", "6th", "7th"];
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorScale = [0, 2, 3, 5, 7, 8, 10];
    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];

    public static int[] Scale(bool minor) => minor ? MinorScale : MajorScale;

    public static int ScaleDegree(int semitonesFromTonic, bool minor)
    {
        var index = Array.IndexOf(Scale(minor), ((semitonesFromTonic % 12) + 12) % 12);
        return index < 0 ? 0 : index + 1;
    }

    public static string Numeral(Chord chord, int keyRoot, bool minor)
    {
        var offset = ((chord.Root - keyRoot) % 12 + 12) % 12;
        var scale = Scale(minor);
        var degree = ScaleDegree(offset, minor);
        string accidental;
        string roman;
        if (degree > 0)
        {
            accidental = "";
            roman = Numerals[degree - 1];
        }
        else
        {
            var below = Array.FindLastIndex(scale, s => s < offset);
            var above = Array.FindIndex(scale, s => s > offset);
            var useFlat = above >= 0 && scale[above] - offset == 1;
            accidental = useFlat ? "♭" : "♯";
            roman = Numerals[useFlat ? above : below];
        }

        var lower = chord.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh or ChordQuality.Diminished;
        var text = lower ? roman.ToLowerInvariant() : roman;
        var suffix = chord.Quality switch
        {
            ChordQuality.Seventh => "7",
            ChordQuality.MajorSeventh => "maj7",
            ChordQuality.MinorSeventh => "7",
            ChordQuality.Suspended => "sus4",
            ChordQuality.Diminished => "°",
            ChordQuality.Power => "5",
            _ => "",
        };
        return accidental + text + suffix;
    }

    public static ChordInfo Describe(Chord chord, int keyRoot, bool minor)
    {
        var offset = ((chord.Root - keyRoot) % 12 + 12) % 12;
        var degree = ScaleDegree(offset, minor);
        var numeral = Numeral(chord, keyRoot, minor);
        var flats = numeral.StartsWith('♭') || (!numeral.StartsWith('♯') && NoteSpelling.KeyUsesFlats(keyRoot, minor));
        var rootName = NoteSpelling.NameFor(chord.Root, flats);
        var fifthName = NoteSpelling.NameFor(chord.Root + 7, flats);
        var name = rootName + chord.QualitySuffix;
        var isMinorChord = chord.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh;
        var isDominantSeventh = chord.Quality == ChordQuality.Seventh;

        var (function, label, headline, text) = Classify(degree, offset, minor, isMinorChord, isDominantSeventh, chord.Quality);
        var title = $"{numeral} · {name}: {headline}";
        var ordinal = degree > 0 ? $"the {Ordinals[degree]} note of the key" : "a note outside the key";
        var tip = $"The root of this chord is {rootName}, {ordinal}. Playing it on the first beat of the bar is the simplest way to hold the song together. The 5th ({fifthName}) and the octave always fit too.";
        return new ChordInfo(numeral, name, function, label, title, text, tip, degree);
    }

    private static (ChordFunction Function, string Label, string Headline, string Text) Classify(int degree, int offset, bool minor, bool minorChord, bool dominantSeventh, ChordQuality quality)
    {
        if (degree == 0)
        {
            var flatSeventh = offset == 10;
            var flatThird = offset == 3;
            var flatSixth = offset == 8;
            if (!minor && (flatSeventh || flatThird || flatSixth) && !minorChord)
            {
                return (ChordFunction.Borrowed, "Borrowed", "a borrowed chord", "A major chord taken from the parallel minor key. It adds a darker, rock colour while the song stays in its key. The flat seven (♭VII) is a rock favourite, as in I – ♭VII – IV.");
            }

            return (ChordFunction.Colour, "Colour", "a chord from outside the key", "This chord is not built from the notes of the key, so it adds surprise. Songs use these for a moment, then return to the chords that belong.");
        }

        if (!minor)
        {
            if (dominantSeventh && degree is 1 or 4 or 5)
            {
                return (ChordFunction.Blues, "Blues", degree == 1 ? "the blues home chord" : degree == 4 ? "the blues 'away' chord" : "the blues tension chord", "Adding a 7th gives the chord a bluesier, slightly unfinished sound. In a 12-bar blues the main chords (I, IV and V) are all played as 7th chords.");
            }

            if (dominantSeventh)
            {
                return (ChordFunction.Tension, "Leads somewhere", "a chord that pulls to the next one", "A major chord with a 7th on a note where the key has a minor chord. It creates a strong pull towards the chord a fifth below it, and is a classic way to add movement.");
            }

            return degree switch
            {
                1 => (ChordFunction.Home, "Home", "the home chord", "The chord the key is built on (the 1st note of the scale). Songs usually start here and feel finished when they come back to it."),
                2 => (ChordFunction.Away, "Away", "a gentle step away", "A minor chord on the 2nd note. It often leads to V, as in ii – V – I, the most common move in jazz."),
                3 => (ChordFunction.SoftHome, "Soft home", "a softer home", "A minor chord on the 3rd note. It shares notes with I, so it sounds like home, only quieter. It often leads on to vi or IV."),
                4 => (ChordFunction.Away, "Away", "the 'away' chord", "A major chord on the 4th note. It feels like a step away from home: calm and open, not tense. It often leads back to I, or on to V."),
                5 => (ChordFunction.Tension, "Tension", "the tension chord", "A major chord on the 5th note. It pulls strongly back to I, so ending on V feels like an unanswered question."),
                6 => (ChordFunction.RelativeMinor, "Relative minor", "the relative minor", "A minor chord on the 6th note. It shares most of its notes with I, so it feels like home with a sadder mood. It is the 'vi' in I – V – vi – IV."),
                _ => (ChordFunction.Tension, "Tension", "an unstable chord", "A diminished chord on the 7th note. It is unstable and wants to resolve up to I, and sounds like a V chord with its root missing."),
            };
        }

        return degree switch
        {
            1 => (ChordFunction.Home, "Home", "the minor home chord", "The chord the key is built on (the 1st note of the minor scale). The song feels settled when it returns here, with a darker mood than a major home."),
            2 => (ChordFunction.Tension, "Unstable", "an unstable chord", "A diminished chord on the 2nd note. It is tense and usually moves on to v or V."),
            3 => (ChordFunction.RelativeMajor, "Relative major", "the relative major", "A major chord on the 3rd note. It shares notes with i, so it brightens the mood without leaving the key."),
            4 => (ChordFunction.Away, "Away", "the 'away' chord", "A minor chord on the 4th note. It is a calm step away from home, and often leads back to i or on to V."),
            5 => minorChord
                ? (ChordFunction.Away, "Soft tension", "a gentle tension chord", "A minor chord on the 5th note. It pulls back to i, softer than the major V, which is common in rock and folk.")
                : (ChordFunction.Tension, "Tension", "the tension chord", "A major chord on the 5th note, borrowed from the harmonic minor scale. It pulls strongly back to i."),
            6 => (ChordFunction.Away, "Away", "a warm step away", "A major chord on the 6th note. It sounds hopeful and open, and often moves on to VII or V."),
            _ => (ChordFunction.Away, "Leads home", "the 'flat seven'", "A major chord a whole step below home. It is a rock favourite, as in i – VII – VI – V, and tends to lead back to i or up to III."),
        };
    }
}
