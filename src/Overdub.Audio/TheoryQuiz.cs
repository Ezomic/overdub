namespace Overdub.Audio;

public enum QuizTopic
{
    Mixed,
    Notes,
    Scales,
    Chords,
    Bass,
    Rhythm,
    Song,
}

public sealed record QuizQuestion(QuizTopic Topic, string Text, IReadOnlyList<string> Options, int Correct, string Explanation, IReadOnlyList<Chord>? Hear = null);

public static class TheoryQuiz
{
    private static readonly (int Semitones, string Name)[] Intervals =
    [
        (3, "minor 3rd"),
        (4, "major 3rd"),
        (5, "perfect 4th"),
        (7, "perfect 5th"),
        (12, "octave"),
    ];

    private static readonly (string Mode, string Clue)[] Modes =
    [
        ("Mixolydian", "a major scale with a flat 7th"),
        ("Dorian", "a minor scale with a raised 6th"),
        ("Lydian", "a major scale with a raised 4th"),
        ("Phrygian", "a minor scale with a flat 2nd"),
    ];

    private static readonly (int Offset, ChordQuality Quality, string Numeral)[] MajorKeyChords =
    [
        (0, ChordQuality.Major, "I"),
        (2, ChordQuality.Minor, "ii"),
        (4, ChordQuality.Minor, "iii"),
        (5, ChordQuality.Major, "IV"),
        (7, ChordQuality.Major, "V"),
        (9, ChordQuality.Minor, "vi"),
    ];

    private static readonly (string Text, string Answer, string[] Wrong, string Why)[] BassFacts =
    [
        ("Which note is the safest to play on beat 1 of a chord?", "The root", ["The 7th", "A passing note", "The note a half step above the root"], "The root names the chord. Playing it on the first beat holds the song together."),
        ("What is an approach note?", "A note a half step from the next chord's root that leads into it", ["The loudest note in the bar", "The first note of the song", "A note played an octave higher"], "Approach notes pull the ear towards the next bar, which is why walking lines use them."),
        ("Which pair of notes is the classic bass pattern for rock and country?", "Root and fifth", ["Root and second", "Third and seventh", "Fourth and sixth"], "The fifth always sounds safe on a chord, so root and fifth is the simplest line that works."),
        ("What does playing the octave of the root add to a bass line?", "Bounce, without changing the harmony", ["A minor sound", "A new chord", "A key change"], "The octave is the same note, so the chord stays the same while the line moves."),
        ("What are the open strings of a 4-string bass, from lowest to highest?", "E A D G", ["G D A E", "A D G C", "E B G D"], "Standard bass tuning is E1, A1, D2, G2, the same as the lowest four guitar strings."),
        ("Which note decides whether a chord sounds major or minor?", "The 3rd", ["The root", "The 5th", "The octave"], "A major 3rd sounds bright, a minor 3rd sounds darker."),
    ];

    private static readonly (string Text, string Answer, string[] Wrong, string Why)[] RhythmFacts =
    [
        ("How many beats are in one bar of 4/4?", "4", ["3", "6", "8"], "The top number of a time signature is the number of beats in a bar."),
        ("How many 8th notes fit in one bar of 4/4?", "8", ["4", "12", "16"], "Each beat holds two 8th notes, and there are four beats."),
        ("Which time signature has the waltz feel?", "3/4", ["4/4", "6/8 with two beats", "2/2"], "A waltz counts one-two-three, with the strong beat on one."),
        ("In a basic rock beat in 4/4, which beats does the snare usually hit?", "2 and 4", ["1 and 3", "Only 1", "Every beat"], "The snare on 2 and 4 is called the backbeat."),
        ("How many 16th notes fit in one beat?", "4", ["2", "3", "8"], "A 16th note is a quarter of a beat."),
        ("What does BPM measure?", "Beats per minute", ["Bars per measure", "Bass pitch in Hz", "Bytes per minute"], "BPM is the tempo of the song: how many beats pass every minute."),
    ];

    private static readonly (string Text, string Answer, string[] Wrong, string Why)[] ChordFacts =
    [
        ("What does a lowercase Roman numeral, like vi, tell you?", "The chord is minor", ["The chord is quieter", "The chord is a 7th", "The chord is outside the key"], "Capitals are major chords and lowercase numerals are minor chords."),
        ("What does the 7 add to a chord like G7?", "A bluesy, unfinished sound", ["A minor sound", "A lower octave", "A faster rhythm"], "The dominant 7th wants to move on to the next chord, which is why the blues is full of them."),
        ("Which chord progression is the most used in pop music?", "I – V – vi – IV", ["I – ii – iii – iv", "ii – I – IV – vii", "iii – II – I – VIII"], "Hundreds of songs use it, in every key."),
    ];

    private static readonly (string Text, string Answer, string[] Wrong, string Why)[] ScaleFacts =
    [
        ("How many notes does a pentatonic scale have?", "5", ["4", "6", "7"], "Penta means five. Dropping two notes from the full scale removes the ones that clash most."),
        ("Compared to the major scale, which notes are flat in the natural minor scale?", "The 3rd, 6th and 7th", ["The 2nd, 4th and 5th", "Only the 3rd", "The 1st and 5th"], "Those three flat notes give minor its darker sound."),
    ];

    public static string TopicName(QuizTopic topic) => topic switch
    {
        QuizTopic.Mixed => "Mixed",
        QuizTopic.Notes => "Notes and intervals",
        QuizTopic.Scales => "Scales and modes",
        QuizTopic.Chords => "Chords and numerals",
        QuizTopic.Bass => "Bass lines",
        QuizTopic.Rhythm => "Rhythm",
        _ => "Your song",
    };

    public static IReadOnlyList<QuizQuestion> Build(QuizTopic topic, int count, Random rng, SongKey? key, IReadOnlyList<Chord> progression)
    {
        var pool = topic == QuizTopic.Mixed
            ? Enum.GetValues<QuizTopic>().Where(t => t != QuizTopic.Mixed && (t != QuizTopic.Song || key is not null)).ToArray()
            : [topic];
        var questions = new List<QuizQuestion>();
        var seen = new HashSet<string>();
        var attempts = 0;
        while (questions.Count < count && attempts++ < count * 40)
        {
            var next = pool[rng.Next(pool.Length)];
            var question = Generate(next, rng, key, progression);
            if (question is not null && seen.Add(question.Text))
            {
                questions.Add(question);
            }
        }

        return questions;
    }

    private static QuizQuestion? Generate(QuizTopic topic, Random rng, SongKey? key, IReadOnlyList<Chord> progression) => topic switch
    {
        QuizTopic.Notes => Notes(rng),
        QuizTopic.Scales => Scales(rng),
        QuizTopic.Chords => Chords(rng),
        QuizTopic.Bass => Bass(rng),
        QuizTopic.Rhythm => Fact(QuizTopic.Rhythm, RhythmFacts, rng),
        _ => key is null ? null : Song(rng, key, progression),
    };

    private static QuizQuestion Fact(QuizTopic topic, (string Text, string Answer, string[] Wrong, string Why)[] facts, Random rng)
    {
        var fact = facts[rng.Next(facts.Length)];
        return Make(topic, fact.Text, fact.Answer, fact.Wrong, fact.Why, rng);
    }

    private static QuizQuestion Make(QuizTopic topic, string text, string correct, IEnumerable<string> wrong, string why, Random rng, IReadOnlyList<Chord>? hear = null)
    {
        var options = new List<string> { correct };
        options.AddRange(wrong.Where(w => w != correct).Distinct().Take(3));
        for (var i = options.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        return new QuizQuestion(topic, text, options, options.IndexOf(correct), why, hear);
    }

    private static string Article(string word, bool capital = false)
    {
        var article = word.StartsWith('o') ? "an" : "a";
        return capital ? char.ToUpper(article[0]) + article[1..] : article;
    }

    private static string Note(int pitchClass, bool flats) => NoteSpelling.NameFor(((pitchClass % 12) + 12) % 12, flats);

    private static bool KeyFlats(int root, bool minor) => NoteSpelling.KeyUsesFlats(root, minor);

    private static IEnumerable<string> OtherNotes(int exceptA, int exceptB, bool flats, Random rng) =>
        Enumerable.Range(0, 12).Where(p => p != exceptA % 12 && p != exceptB % 12).OrderBy(_ => rng.Next()).Select(p => Note(p, flats));

    private static QuizQuestion Notes(Random rng)
    {
        var root = rng.Next(12);
        var flats = KeyFlats(root, false);
        var (semitones, name) = Intervals[rng.Next(Intervals.Length)];
        switch (rng.Next(3))
        {
            case 0:
                var answer = Note(root + semitones, flats);
                return Make(QuizTopic.Notes, $"Which note is {Article(name)} {name} above {Note(root, flats)}?", answer, OtherNotes(root + semitones, root, flats, rng), $"{Article(name, true)} {name} is {semitones} semitones, so from {Note(root, flats)} you count {semitones} frets up and land on {answer}.", rng);
            case 1:
                return Make(QuizTopic.Notes, $"How many semitones (frets on the bass) is {Article(name)} {name}?", semitones.ToString(), Intervals.Select(i => i.Semitones.ToString()).Concat(["2", "6"]), $"{Article(name, true)} {name} is {semitones} frets away from the starting note.", rng);
            default:
                return Make(QuizTopic.Notes, $"What is the interval of {semitones} semitones called?", name, Intervals.Select(i => i.Name).Concat(["tritone"]), $"{semitones} semitones is a {name}.", rng);
        }
    }

    private static QuizQuestion Scales(Random rng)
    {
        var root = rng.Next(12);
        var flats = KeyFlats(root, false);
        var scale = Theory.Scale(false);
        var keyName = Note(root, flats);
        switch (rng.Next(5))
        {
            case 0:
                var degree = rng.Next(1, 7);
                var ordinals = new[] { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th" };
                var answer = Note(root + scale[degree], flats);
                var inScale = scale.Select(s => Note(root + s, flats)).Where(n => n != answer);
                return Make(QuizTopic.Scales, $"What is the {ordinals[degree]} note of the {keyName} major scale?", answer, inScale.OrderBy(_ => rng.Next()), $"The {keyName} major scale is {string.Join(" ", scale.Select(s => Note(root + s, flats)))}. The {ordinals[degree]} note is {answer}.", rng);
            case 1:
                var outside = Enumerable.Range(0, 12).Where(p => !scale.Contains(((p - root) % 12 + 12) % 12)).ToList();
                var odd = Note(outside[rng.Next(outside.Count)] , flats);
                var inside = scale.Select(s => Note(root + s, flats)).OrderBy(_ => rng.Next());
                return Make(QuizTopic.Scales, $"Which note is NOT in the {keyName} major scale?", odd, inside, $"The {keyName} major scale is {string.Join(" ", scale.Select(s => Note(root + s, flats)))}, so {odd} is outside it.", rng);
            case 2:
                var (mode, clue) = Modes[rng.Next(Modes.Length)];
                return Make(QuizTopic.Scales, $"Which mode is {clue}?", mode, Modes.Select(m => m.Mode), $"{mode} is {clue}. It gives lines their own colour while staying close to the major or minor scale.", rng);
            case 3:
                var minorName = Note(root + 9, KeyFlats(root + 9, true));
                return Make(QuizTopic.Scales, $"{keyName} major has the same notes as which minor key?", $"{minorName} minor", OtherNotes(root + 9, root, flats, rng).Select(n => n + " minor"), $"{minorName} minor is the relative minor of {keyName} major: the same seven notes, starting on the 6th.", rng);
            default:
                return Fact(QuizTopic.Scales, ScaleFacts, rng);
        }
    }

    private static QuizQuestion Chords(Random rng)
    {
        if (rng.Next(3) == 0)
        {
            return Fact(QuizTopic.Chords, ChordFacts, rng);
        }

        var root = rng.Next(12);
        var flats = KeyFlats(root, false);
        var (offset, quality, numeral) = MajorKeyChords[rng.Next(MajorKeyChords.Length)];
        var chord = new Chord(root + offset, quality);
        var name = ChordName(chord, flats);
        var keyName = Note(root, flats);
        var hear = new[] { chord };
        if (rng.Next(2) == 0)
        {
            return Make(QuizTopic.Chords, $"In the key of {keyName} major, which chord is the {numeral}?", name, MajorKeyChords.Where(c => c.Numeral != numeral).Select(c => ChordName(new Chord(root + c.Offset, c.Quality), flats)).OrderBy(_ => rng.Next()), $"The {keyName} major scale gives {string.Join(", ", MajorKeyChords.Select(c => ChordName(new Chord(root + c.Offset, c.Quality), flats)))} as I, ii, iii, IV, V, vi. The {numeral} is {name}.", rng, hear);
        }

        return Make(QuizTopic.Chords, $"In the key of {keyName} major, what is the Roman numeral of {name}?", numeral, Numeral.Pool(numeral, rng), $"{name} is built on the {Position(offset)} note of {keyName} major, and {(quality == ChordQuality.Minor ? "a minor chord gets a lowercase numeral" : "a major chord gets a capital")}, so it is {numeral}.", rng, hear);
    }

    private static string ChordName(Chord chord, bool flats) => Note(chord.Root, flats) + chord.QualitySuffix;

    private static string Position(int offset) => offset switch
    {
        0 => "1st",
        2 => "2nd",
        4 => "3rd",
        5 => "4th",
        7 => "5th",
        _ => "6th",
    };

    private static QuizQuestion Bass(Random rng)
    {
        if (rng.Next(3) > 0)
        {
            return Fact(QuizTopic.Bass, BassFacts, rng);
        }

        var root = rng.Next(12);
        var flats = KeyFlats(root, false);
        var answer = Note(root + 7, flats);
        return Make(QuizTopic.Bass, $"The chord is {Note(root, flats)}. Which note is its fifth, the safe second choice for the bass?", answer, OtherNotes(root + 7, root, flats, rng), $"The fifth is 7 frets above the root: {Note(root, flats)} to {answer}.", rng);
    }

    private static QuizQuestion? Song(Random rng, SongKey key, IReadOnlyList<Chord> progression)
    {
        var flats = NoteSpelling.Mode switch { 1 => false, 2 => true, _ => KeyFlats(key.Root, key.Minor) };
        var keyName = key.Name;
        var chords = progression.Distinct().ToList();
        var choice = rng.Next(chords.Count > 0 ? 3 : 1);
        if (choice == 0 || chords.Count == 0)
        {
            var degree = rng.Next(1, 7);
            var scale = Theory.Scale(key.Minor);
            var ordinals = new[] { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th" };
            var answer = Note(key.Root + scale[degree], flats);
            return Make(QuizTopic.Song, $"Your song is in {keyName}. What is the {ordinals[degree]} note of its scale?", answer, scale.Select(s => Note(key.Root + s, flats)).Where(n => n != answer).OrderBy(_ => rng.Next()), $"The scale of {keyName} is {string.Join(" ", scale.Select(s => Note(key.Root + s, flats)))}. The {ordinals[degree]} note is {answer}.", rng);
        }

        var chord = chords[rng.Next(chords.Count)];
        var name = Note(chord.Root, flats) + chord.QualitySuffix;
        var numeral = Theory.Numeral(chord, key.Root, key.Minor);
        return Make(QuizTopic.Song, $"Your song is in {keyName}. What is the Roman numeral of the {name} chord?", numeral, Numeral.Pool(numeral, rng), $"{name} sits at \"{numeral}\" in {keyName}: the number shows its place in the scale, and capitals or lowercase show major or minor.", rng, [chord]);
    }

    private static class Numeral
    {
        private static readonly string[] All = ["I", "ii", "iii", "IV", "V", "vi", "vii°", "♭VII", "♭III", "II", "III", "VI", "i", "iv", "v", "VII"];

        public static IEnumerable<string> Pool(string correct, Random rng)
        {
            var flipped = correct.ToLowerInvariant() == correct ? correct.ToUpperInvariant() : correct.ToLowerInvariant();
            var others = All.Where(n => n != correct && n != flipped).OrderBy(_ => rng.Next());
            return new[] { flipped }.Concat(others);
        }
    }
}
