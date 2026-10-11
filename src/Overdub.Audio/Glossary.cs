namespace Overdub.Audio;

public sealed record GlossaryTerm(string Name, string Category, string Definition, string? Example, string? Lesson, string[] Aliases);

public static class Glossary
{
    public static readonly string[] Categories = ["Notes and scales", "Chords", "Bass", "Rhythm", "Song form", "Recording"];

    private const string Numerals = "Start here: understand your song";
    private const string BassLine = "Why this bass line works";
    private const string Explorer = "Scale and mode explorer";
    private const string Circle = "Circle of fifths and progression builder";
    private const string Ear = "Ear trainer";
    private const string RhythmLesson = "Rhythm and time signatures";
    private const string Structure = "Song structure by style";
    private const string Challenges = "Practice challenges";

    public static IReadOnlyList<GlossaryTerm> All { get; } =
    [
        new("Semitone", "Notes and scales", "The smallest step between two notes. On a bass it is one fret. Also called a half step.", "From E to F is one semitone.", Ear, ["semitone", "semitones", "half step", "half steps", "half-step"]),
        new("Whole step", "Notes and scales", "Two semitones, or two frets on a bass.", "From E to F# is a whole step.", Explorer, ["whole step", "whole steps"]),
        new("Octave", "Notes and scales", "The same note, higher or lower. It is 12 frets away and has twice or half the frequency.", "The A on the open A string and the A at fret 12 are an octave apart.", Ear, ["octave", "octaves"]),
        new("Interval", "Notes and scales", "The distance between two notes, named by how many scale steps apart they are.", "C to G is a perfect 5th.", Ear, ["interval", "intervals"]),
        new("Root", "Notes and scales", "The note a chord or scale is named after and built on. It is the 1 of the scale and the note that sounds like home.", "The root of an A minor chord is A.", Numerals, ["root", "roots"]),
        new("Third", "Notes and scales", "The note three scale steps above the root. It decides whether a chord sounds major (bright) or minor (darker).", "In C major the third is E. In C minor it is E flat.", BassLine, ["third", "3rd", "thirds"]),
        new("Fifth", "Notes and scales", "The note seven semitones, or seven frets, above the root. It always sounds safe on a chord.", "The fifth of C is G.", BassLine, ["fifth", "5th", "fifths"]),
        new("Seventh", "Notes and scales", "The note a seventh above the root. A flat seventh sounds bluesy and unfinished, a major seventh sounds dreamy.", "The flat seventh of C is B flat.", BassLine, ["seventh", "7th", "sevenths"]),
        new("Scale", "Notes and scales", "A set of notes, usually seven, arranged by step from the root up to the octave. Songs are built from the notes of a scale.", "The C major scale is C D E F G A B.", Explorer, ["scale", "scales"]),
        new("Key", "Notes and scales", "The scale a song is built on, and the root that feels like home. A song in the key of C mostly uses the notes of C major.", "A song in A minor ends on an A minor chord.", Circle, ["key"]),
        new("Relative minor", "Notes and scales", "The minor key that shares its notes with a major key. It starts on the 6th note of the major scale.", "A minor is the relative minor of C major.", Circle, ["relative minor"]),
        new("Mode", "Notes and scales", "A scale built from the same notes as a major scale but starting on a different note, which gives it a different colour.", "Dorian is the major scale starting on its 2nd note.", Explorer, ["mode", "modes"]),
        new("Pentatonic scale", "Notes and scales", "A scale with only five notes. Taking out the notes that clash makes it hard to play a wrong note, so it is the go-to for riffs and solos.", "A minor pentatonic is A C D E G.", Explorer, ["pentatonic"]),
        new("Blue note", "Notes and scales", "A note added to the minor pentatonic scale, the flat fifth. Passing through it gives the blues its tension.", "In A blues the blue note is E flat.", Explorer, ["blue note"]),
        new("Sharp", "Notes and scales", "A sign that raises a note by one semitone.", "F sharp is one fret above F.", null, ["sharp", "sharps"]),
        new("Flat", "Notes and scales", "A sign that lowers a note by one semitone.", "B flat is one fret below B.", null, ["flat", "flats"]),
        new("Circle of fifths", "Notes and scales", "A circle of the twelve keys, each a fifth above the one before. Keys that sit next to each other share all but one note.", "C is next to G and F on the circle.", Circle, ["circle of fifths"]),

        new("Chord", "Chords", "Three or more notes played together. The root is the note it is named after.", "C E G is a C major chord.", Numerals, ["chord", "chords"]),
        new("Triad", "Chords", "A chord of three notes: a root, a third and a fifth.", "A C major triad is C E G.", Numerals, ["triad", "triads"]),
        new("Major chord", "Chords", "A chord with a major third. It sounds bright and settled.", "C major is C E G.", Ear, ["major chord", "major chords"]),
        new("Minor chord", "Chords", "A chord with a minor third, a semitone lower than major. It sounds darker and sadder.", "A minor is A C E.", Ear, ["minor chord", "minor chords"]),
        new("Seventh chord", "Chords", "A chord with a seventh added on top of a triad. A dominant seventh sounds bluesy and wants to move on.", "G7 is G B D F.", Ear, ["seventh chord", "dominant 7th"]),
        new("Suspended chord", "Chords", "A chord where the third is replaced by the fourth. It hangs in the air and wants to resolve.", "Csus4 is C F G.", Ear, ["suspended", "sus4"]),
        new("Diminished chord", "Chords", "A chord made of two minor thirds stacked. It sounds tense and unstable.", "B diminished is B D F.", Ear, ["diminished"]),
        new("Power chord", "Chords", "A chord of only the root and the fifth, with no third, so it is neither major nor minor. Common in rock and punk.", "A power chord on E is E and B.", null, ["power chord", "power chords"]),
        new("Roman numeral", "Chords", "A way to name a chord by its place in the key. A capital numeral is a major chord and a lowercase one is a minor chord.", "In C major, Am is vi.", Numerals, ["roman numeral", "roman numerals", "numeral", "numerals"]),
        new("Progression", "Chords", "A series of chords played one after another.", "I V vi IV is a famous pop progression.", Circle, ["progression", "progressions"]),
        new("Diatonic", "Chords", "Belonging to the key: made only of notes in its scale.", "In C major, Dm is diatonic and D major is not.", Circle, ["diatonic"]),
        new("Tonic", "Chords", "The home chord or note of a key, the I.", "In C major the tonic is C.", Numerals, ["tonic", "home chord"]),
        new("Dominant", "Chords", "The chord built on the fifth note of the key, the V. It creates tension that wants to return to the tonic.", "In C major the dominant is G.", Numerals, ["dominant"]),
        new("Borrowed chord", "Chords", "A chord taken from the parallel minor (or major) key to add colour while the song stays in its key.", "The flat VII chord in a major key is borrowed from minor.", Numerals, ["borrowed chord", "borrowed"]),
        new("Turnaround", "Chords", "A short passage at the end of a section that leads back to the beginning.", "The last two bars of a 12-bar blues are a turnaround.", Structure, ["turnaround"]),

        new("Approach note", "Bass", "A note a half step or a whole step from the note you are heading to, played just before it to lead the ear in.", "B just before C.", BassLine, ["approach note", "approach notes", "approach"]),
        new("Passing note", "Bass", "A note that is not part of the chord, used to connect the notes around it. It sounds smooth because it is short.", "D between C and E.", BassLine, ["passing note", "passing notes"]),
        new("Walking bass", "Bass", "A bass line that moves note by note, usually in steady quarter notes, walking from one chord to the next. Common in jazz and blues.", null, BassLine, ["walking bass", "walking line"]),
        new("Riff", "Bass", "A short repeating pattern of notes that gives a song its identity.", "The opening bass line of a funk song.", null, ["riff", "riffs"]),
        new("Bass line", "Bass", "The notes the bass plays through a song. It outlines the chords and works with the drums to carry the rhythm.", null, BassLine, ["bass line", "bass lines"]),
        new("Groove", "Bass", "The rhythmic feel of the band playing together, especially how the bass and drums lock in.", null, Challenges, ["groove"]),
        new("Pocket", "Bass", "Playing exactly in the groove, steadily on the beat, so the band feels locked together.", null, Challenges, ["pocket"]),
        new("Ghost note", "Bass", "A very quiet, muted note that adds rhythm without a pitch you can hear clearly.", null, null, ["ghost note", "ghost notes"]),
        new("Tab", "Bass", "A way of writing music for a bass that shows which string and fret to play, instead of note names.", "A 3 on the A string means the third fret.", null, ["tab", "tablature"]),
        new("Fret", "Bass", "A metal strip on the neck. Pressing a string just behind a fret gives a note. Each fret is one semitone.", null, null, ["fret", "frets"]),
        new("Slap", "Bass", "Playing technique where you hit the string with your thumb and pop it with a finger for a bright, percussive sound. Common in funk.", null, null, ["slap"]),

        new("Beat", "Rhythm", "The steady pulse you tap your foot to. Music is counted in beats.", null, RhythmLesson, ["beat", "beats"]),
        new("Bar", "Rhythm", "One group of beats, such as four beats in 4/4. Chords usually change at the start of a bar. Also called a measure.", null, RhythmLesson, ["bar", "bars"]),
        new("Tempo", "Rhythm", "How fast the beat is, measured in beats per minute.", "104 BPM is a medium tempo.", RhythmLesson, ["tempo"]),
        new("BPM", "Rhythm", "Beats per minute: how many beats pass every minute.", null, RhythmLesson, ["bpm"]),
        new("Time signature", "Rhythm", "Two numbers that say how many beats are in a bar and what note counts as one beat.", "4/4 has four quarter-note beats per bar.", RhythmLesson, ["time signature", "time signatures", "meter"]),
        new("Downbeat", "Rhythm", "The first beat of a bar, usually the strongest.", null, RhythmLesson, ["downbeat"]),
        new("Backbeat", "Rhythm", "Strong beats 2 and 4 in a 4/4 bar, where the snare usually hits in rock and pop.", null, RhythmLesson, ["backbeat"]),
        new("Subdivision", "Rhythm", "Splitting a beat into smaller equal parts, such as two eighth notes or three triplets.", null, RhythmLesson, ["subdivision", "subdivisions"]),
        new("Syncopation", "Rhythm", "Putting the emphasis on weak beats or off-beats, so the rhythm feels like it is pulling against the pulse.", null, null, ["syncopated", "syncopation"]),
        new("Triplet", "Rhythm", "Three equal notes played in the time of two, giving a rolling feel.", null, RhythmLesson, ["triplet", "triplets"]),
        new("Quarter note", "Rhythm", "A note that lasts one beat in 4/4.", null, RhythmLesson, ["quarter note", "quarter notes"]),
        new("Eighth note", "Rhythm", "A note that lasts half a beat in 4/4, so there are two per beat.", null, RhythmLesson, ["eighth note", "eighth notes", "eighths"]),
        new("Sixteenth note", "Rhythm", "A note that lasts a quarter of a beat in 4/4, so there are four per beat.", null, RhythmLesson, ["sixteenth note", "sixteenth notes"]),

        new("Intro", "Song form", "The opening of a song, before the first verse. It sets the key, tempo and mood.", null, Structure, ["intro"]),
        new("Verse", "Song form", "The part of a song where the words change each time while the music stays the same. It tells the story.", null, Structure, ["verse", "verses"]),
        new("Chorus", "Song form", "The part of a song that repeats the title and main melody. It is usually the loudest and the most memorable.", null, Structure, ["chorus", "choruses"]),
        new("Hook", "Song form", "The catchiest part of a song, the bit people remember. Often the chorus.", null, Structure, ["hook"]),
        new("Bridge", "Song form", "A contrasting section, usually with different chords, that leads into the last chorus.", null, Structure, ["bridge"]),
        new("Solo", "Song form", "A section where one instrument plays the melody or improvises while the rest of the band supports it.", null, Structure, ["solo"]),
        new("Breakdown", "Song form", "A section where most of the band drops out, so the return of the full band feels bigger.", null, Structure, ["breakdown"]),
        new("Build", "Song form", "A section in dance music that adds layers one at a time to raise tension before the drop.", null, Structure, []),
        new("Drop", "Song form", "The payoff in dance music, where everything comes in at once after a build.", null, Structure, []),
        new("Outro", "Song form", "The ending of a song, which winds it down instead of stopping suddenly.", null, Structure, ["outro"]),
        new("Arrangement", "Song form", "The way a song is laid out: which instruments play in which sections and what each one plays.", null, Structure, ["arrangement"]),

        new("Count-in", "Recording", "Clicks before a recording starts so you can come in on time. In Overdub it is one bar.", null, Challenges, ["count-in", "count in"]),
        new("Latency", "Recording", "The small delay between playing a note and hearing it, caused by the audio hardware and software. Lower is better.", "11.6 ms is a low latency.", null, ["latency"]),
        new("Gain", "Recording", "How loud a signal is. Input gain sets how loud your instrument is recorded.", null, null, ["gain"]),
        new("Pan", "Recording", "Where a sound sits between the left and right speakers.", null, null, ["pan", "panning"]),
        new("Reverb", "Recording", "An effect that makes a sound seem to be in a room, with a tail that fades out.", null, null, ["reverb"]),
        new("Delay", "Recording", "An effect that repeats a sound after a short time, like an echo.", null, null, ["delay"]),
        new("Compression", "Recording", "An effect that makes loud parts quieter and quiet parts louder, so the volume is more even.", null, null, ["compression", "compressor"]),
        new("EQ", "Recording", "Equalization: boosting or cutting parts of the sound, such as the low end or the highs.", null, null, ["eq", "equalizer"]),
        new("Metronome", "Recording", "A steady click that keeps time while you play.", null, RhythmLesson, ["metronome"]),
        new("Punch in", "Recording", "Recording over just one part of a take instead of the whole thing.", null, null, ["punch in"]),
    ];

    public static GlossaryTerm? Find(string name) => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<(string Alias, GlossaryTerm Term)> Aliases() =>
        All.SelectMany(t => t.Aliases.Select(a => (a, t))).OrderByDescending(x => x.a.Length);
}
